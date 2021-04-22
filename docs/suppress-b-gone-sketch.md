# Suppress B Gone

For the assumed background knowledge, please consult the:
- [glossary](glossary.md) for definitions of terms used in this document

This document sketches out some basic ideas for how to eliminate the need for suppression, and along with that the need for `*` buckets.

## Background

Low Count Filtering (LCF) as we implemented it in Insights serves two purposes:

1. It prevents the reporting of unique values (i.e. `SELECT ssn FROM table`). It does this by suppressing the associated bucket altogether.
2. It prevents attackers from inferring whether an answer has 0 versus 1 user (or 1 versus 2 users, etc.). It does so using a noisy LCF threshold.

### Issues

This approach has several issues.

1. It can hide large amounts of data. This can give the analyst the impression that a lot of data that does exist doesn't. We tried to address this shortcoming by aggregated suppressed buckets into `*` or `NULL` buckets, but this approach is unsatisfactory for two reasons:
  a. It is confusing to the analyst.
  b. It doesn't necessarily mimic how SQL should behave.
2. It hides column values that don't need to be hidden. In other words, it may suppress a value that could otherwise be learned with for instance a broader search.

## A new approach

### Characteristics

This doc sketches out a new approach with the following characteristics:

1. All answers produce an approximately correct number of buckets.
2. The bucket values (the GROUP BY columns) may or may not represent real bucket values, but do so to the extent possible.
3. A bucket with a real value but with zero contributing rows (i.e. one that would normally not be output) may be in the answer. Such buckets, however, should be selected among enough other such buckets that nothing about how many AID values comprise the bucket can be inferred with high probability.

By way of example, consider the query:

```sql
SELECT lastname, count(*)
FROM table
WHERE age = 25
GROUP BY 1
```

With Insights, this would produce a table like this:

| lastname | count |
| -------- | ----: |
| `*`      | 19221 |
| Smith    |   133 |
| Jones    |    98 |
| Miller   |    32 |
| Williams |     7 |

With the new approach, the table might be like this:

| lastname | count |     | comments                           |
| -------- | ----: | --- | ---------------------------------- |
| Smith    |   133 |     |                                    |
| Jones    |    98 |     |                                    |
| Miller   |    32 |     |                                    |
| Johnson  |     5 |     | Was suppressed in old approach     |
| Williams |     7 |     |                                    |
| Brown    |     3 |     | There are zero Browns with age 25! |
| ...      |   ... |     |                                    |
| SmXXX    |     2 |     | Not a valid name                   |
| MXXXXX   |     1 |     | Not a valid name                   |
| ...      |   ... |     |                                    |

Note that the `*` bucket is gone. Johnson was suppressed before, but now shows up. This is because there are enough Johnson's in the whole table to be able to report the name.  Same with Brown, but there may in fact be zero Browns of age 25. In addition to reporting real names, the system may also report synthesized names, to replace names that don't have enough users to report under any circumstances. (These synthesized names might have a "wildcard" character like 'XXX', or could in fact have characters taken from the distribution of actual characters.)

### Basic approach

The basic approach uses a new concept which I'll call *showable*. A value is showable if there are *any* circumstances under which the analyst could have learned the value using Diffix. For instance, in the `lastname` example, an analyst could learn `lastname=Brown` with the query `SELECT DISTINCT lastname FROM table`, even though all Browns are filtered with the condition `age = 25`. So in other words, `Brown` is showable.

With this in mind, let's consider a basic approach. The following example is for the selected column lastname. This is a categorical column with many values. As a categorical column, the values aren't naturally aggregatable (unlike numeric or datetime columns). We look at numeric and datatime after this example.

During a query, the query engine examines d-rows. Some of these are included in a given bucket (`age=25`) and some are excluded (`age<>25`). Suppose during execution we record both the selected values that are included and those that are excluded, along with the associated number of distinct AID values. For the above query, we could end up with the following table:

| lastname  | count | AID values-included | AID values-excluded | showable |
| --------- | ----: | ------------------: | ------------------- | -------- |
| Smith     |   133 |                  29 | 439                 | yes      |
| Jones     |    98 |                  22 | 318                 | yes      |
| ...       |       |                 ... | ...                 | ...      |
| Andrews   |    14 |                   2 | 112                 | yes      |
| Brown     |     7 |                   1 | 101                 | yes      |
| Black     |     9 |                   1 | 110                 | yes      |
| ...       |       |                 ... | ...                 | ...      |
| Pinker    |    10 |                   2 | 1                   | no       |
| Schmidt   |     3 |                   1 | 0                   | no       |
| Franklin  |     5 |                   1 | 0                   | no       |
| ...       |       |                 ... | ...                 | ...      |
| Fisher    |     0 |                   0 | 8                   | yes      |
| Barker    |     0 |                   0 | 4                   | yes      |
| Peters    |     0 |                   0 | 7                   | yes      |
| ...       |       |                 ... | ...                 | ...      |
| Snodgrass |     0 |                   0 | 1                   | no       |
| Snodblade |     0 |                   0 | 2                   | no       |
| Snodtree  |     0 |                   0 | 2                   | no       |
| ...       |       |                 ... | ...                 | ...      |

From `AID values-included + AID values-excluded`, we can determine if each name is showable. (Assume here that a given AID value is not counted in AID values-excluded if it is in AID values-included.)

Define a modified LCF computation LCF-soft, where instead of having a hard lower bound on AID value count, we include buckets that have an AID value count of zero so long as they are showable. (Unless otherwise stated, 'LCF' refers to 'LCF-soft'.)

The following table adds a column with the resulting LCF noisy thresholds, where a *threshold* here is simple the AID values-included count with noise added:

| lastname  | count | AID values-included | AID values-excluded | showable | threshold | type   |
| --------- | ----: | ------------------: | ------------------: | -------- | --------: | ------ |
| Smith     |   133 |                  29 |                 439 | yes      |        30 | type 1 |
| Jones     |    98 |                  22 |                 318 | yes      |        21 |        |
| ...       |   ... |                 ... |                 ... | ...      |       ... | ...    |
| Andrews   |    14 |                   2 |                 112 | yes      |         5 | type 2 |
| Brown     |     7 |                   1 |                 101 | yes      |        -1 |        |
| Black     |     9 |                   1 |                 110 | yes      |         1 |        |
| ...       |   ... |                 ... |                 ... | ...      |       ... | ...    |
| Pinker    |    10 |                   2 |                   1 | no       |         3 | type 3 |
| Schmidt   |     3 |                   1 |                   0 | no       |        -2 |        |
| Franklin  |     5 |                   1 |                   0 | no       |         0 |        |
| ...       |   ... |                 ... |                 ... | ...      |       ... | ...    |
| Fisher    |     0 |                   0 |                   8 | yes      |         4 | type 4 |
| Barker    |     0 |                   0 |                   4 | yes      |         1 |        |
| Peters    |     0 |                   0 |                   7 | yes      |         8 |        |
| ...       |   ... |                 ... |                 ... | ...      |       ... | ...    |
| Snodgrass |     0 |                   0 |                   1 | no       |         - | type 5 |
| Snodblade |     0 |                   0 |                   2 | no       |         - |        |
| Snodtree  |     0 |                   0 |                   2 | no       |         - |        |
| ...       |   ... |                 ... |                 ... | ...      |       ... | ...    |

These are shown in groups of different types (the importance of which is discussed shortly):

Type 1. Buckets like Smith that pass LCF.
Type 2. Buckets like Andrews that do not pass LCF but have at least one AID value and are showable.
Type 3. Buckets like Pinker that do not pass LCF and have at least one AID value but are not showable.
Type 4. Buckets like Fisher that have zero AID values and so certainly do not pass LCF, but are still showable.
Type 5. Buckets like Snodgrass that have zero AID values and are not showable.

Note that any of these types may or may not be present for any given query.

Now what we could do is the following.

1. Compute the number of buckets N the output should have. N is some noisy number close to (or in many cases identical to) the true number of buckets that a non-anonymizing query would output (i.e. all of those with non-zero AID values-included).
2. Rank order the showable buckets by threshold descending.
3. Report the first N buckets as output with associated noisy counts.

So, to continue the above example, the ranked list might be:

| lastname | AID values-included | AID values-excluded | showable | threshold |
| -------- | ------------: | ------------: | -------- | --------: |
| Smith    |            29 |           439 | yes      |        30 |
| Jones    |            22 |           318 | yes      |        21 |
| ...      |           ... |           ... | ...      |       ... |
| Peters   |             0 |             7 | yes      |         8 |
| Andrews  |             2 |           112 | yes      |         5 |
| Fisher   |             0 |             8 | yes      |         4 |
| Black    |             1 |           110 | yes      |         1 |
| Barker   |             0 |             4 | yes      |         1 |
| Brown    |             1 |           101 | yes      |        -1 |
| ...      |           ... |           ... | ...      |       ... |

If the top N rows includes through Black, then those rows would be in the output (with associated noisy counts), and Barker onwards would be excluded.

A key observation here is that the number of AID values-included does not have the same ordering as the LCF threshold. As a result, buckets with zero users are interspersed with buckets with one or more users. This results in uncertainty on the part of the analyst as to which buckets have no users versus one user, or one user versus two users, etc. (At this point in time I'm not prepared to say that this is adequate uncertainty for Cloak or Knox. Note also that my latest thinking for LED is that we'll adjust values associated with AID values, and that would lead to far more uncertainty.)

### Not enough showable buckets

Of course it could happen that there are not enough showable buckets to reach N. In this case, we need to generate synthetic values from the non-showable data that we have (ideally what we have after normal query processing).

As a general rule, if we are going to build synthetic values it is better to do so from data that matches as many query conditions as possible because these values may be more accurate in some respect. This is probably not normally the case with relatively un-correlated data like lastnames, but might be the case with other data like numeric or datetime.

In the example above, any d-rows contributing to AID values-included fully match the query conditions. D-rows contributing to AID values-excluded do not match all conditions, but may match some. The more matching conditions the better.

### Showable but nonsensical buckets

There can clearly be cases where buckets are showable, but don't really make sense in the context of the query. An example is this query:

```
SELECT city, count(*)
FROM persons
WHERE age = 10 and country = 'Germany'
```

If cities listed include New York, Seattle, and Oslo, then the answer is nonsensical and confusing.

One idea to deal with this would be to detect strong correlations between selected and filtered columns, and focus the result on those rows that match the strongly-correlated condition. For instance, in the above query, there would be essentially no correlation between city and age. There would be a strong correlation, however, between city and country (not perfect, since there are for instance multiple Berlin's in the USA), but the correlation would be strong. In this case, we could limit ourselves to cities that are True for `country = 'Germany'`.

### Multiple GROUP BY columns

The concept of showable extends to the case with multiple GROUP BY columns. For instance, if there query is:

```
SELECT make, model
FROM car_purchases
WHERE age = 30 and gender = 'M'
```

then we would look for cases where `make` and `model` together are showable (i.e. Ford/Mustang and Tesla/Model S). If the GROUP BY columns are strongly correlated, then this should happen rather naturally. If not, then the fact that the value from one column doesn't make sense with respect to a value from another column won't be as confusing.

If the GROUP BY columns are aggregatable and correlated, then we'd want to produce synthetic values that reflect the correlation.

### Possible issues

This is a substantial departure from how we deal with low-count buckets in the past. It is also a substantial departure from how we distort answers. Let's look at the anonymity issues first.

From the table above, we see that we now report buckets that have one or zero users. The anonymity principle we are relying on here is that an attacker can't confidently distinguish between buckets that have 0 or 1 user (or K versus K-1 users for any value of K).

> TODO: add more here

### Generating synthetic bucket values

There are two broad approaches we can make to generating synthetic values. One is to use "wildcard" characters like 'XXX' or '00:00:00'. The other is to replace characters from the population of characters. The first has the advantage of hinting that the value is synthetic. The second has the advantage of having the 'look-and-feel' of real data. Note that in any event we should have boolean columns that indicate whether a value is synthetic or not.

The details on how to do this can vary by a lot. I think a basic approach is to build synthetic values by sampling from two aspects of the raw data:

1. Length of the values (number of characters).
2. Character sets in each character position.

We can sample from from these aspects so long as the number of AID values for each aspect passes LCF (both included and excluded AID values).

So for instance, if the value is lastname, then there are a variety of lastname lengths from two characters upwards to 30 or 40. But we'd want to ensure that for each lastname length, we have an adequate number of distinct AID values, and only select the lengths of synthetic strings from passing lengths.

Once we have a value length, we could sample from the characters that occupy the corresponding position in the string.

For instance, for last names, the first position might sample from characters A-Z, while the second position samples from a-z. (But only those letters that have enough AID values in the corresponding position.)

Of course, we also want the values to be syntactically correct. Numbers should have one or zero decimal points, email addresses should have '@' mark and one or more dots. In this case, we care about field lengths instead of the length of the entire string. For instance, the length of the string after the last dot or before the '@' of an email address, the number of digits before and after the decimal point, etc.

> TODO: Lots and lots of details about how to actually do this
