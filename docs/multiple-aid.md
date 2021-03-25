Please consult the [glossary](glossary.md) for definitions of terms used in this document.

# Computing Per-AID Contributions

With an integrated cloak, we go back to the original style of computing noise, where we have information about individual AIDs. This is needed to compute noise about the various aggregates, including future aggregates and even user-defined aggregates. There are at least two types of aggregates:

1. Combined: An aggregate that is derived from multiple values. `sum()` is an example.
2. Individual: An aggregate that is derived from a single value. `median()` is an example.

> TODO: there may be other types. Maybe should have a look at a bunch of aggregates and see.


## Multiple AIDs

A table may have one or more AID columns (columns labeled as being AIDs). When there is more than one AID in a query (either because there are multiple AIDs in a table or tables have been joined), by default, Diffix treats them as distinct. In other words, as though they refer to different entities. In so doing, Diffix handles AIDs of different types seamlessly, and also is robust in cases where `JOIN` operations incorrectly mix AIDs together (i.e. a row for user1 is joined with a row for user2).

We use the nomenclature `AIDX[Y1]` to describe that a row has AID column `AIDX` and that it belongs to the entity identified by `Y1`. For example `AIDX` might be `send_email` and `Y1` might be `sue1@gmail.com`, in which case a row might have the AID `send_email[sue1@gmail.com]`.
If the table additionally contains a second AID column `recipient_email` then the same row might be fully described through the pair of AIDs `[send_email[sue1@gmail.com]; recipient_email[bob6@yahoo.com]]`.

If a table is joined with itself, then the AIDs from the left and right side of the join are treated as distinct.
You might therefore end up with `[send_emailL[sue1@gmail.com]; send_emailR[sue1@gmail.com]]` for the same row. While both refer to the same entity, our system still treats them as separate AIDs.

Through aggregation a row might be associated with multiple AID values for a single type of AID. Should we aggregate the aforementioned email table by the day of the week, we might for example end up with a row for Mondays with an AID set such as `[send_email[sue1@gmail.com, bob6@yahoo.com, liz@hey.com, esmeralda@icloud.com]]` indicating that `sue1`, `bob`, `liz` as well as `esmeralda` sent one or more emails on that day.


### Low count filter

Low count filtering is done per AID type individually. For a bucket with the AID sets `[email1; email2; email3; ssn1; ssn2]` all five AID sets individually need to pass the low count threshold. Each kind of AID set might have a distinct  `minimum_allowed_aids` parameter (i.e. the parameter for `email` might differ from that of `ssn`).


## How AIDs spread

### JOINing rows

When joining rows from two relations (tables, views, etc, see the glossary for a definition), the combined row's AID sets is the union of the AID sets of the rows being joined. Crucially AID sets of the same kind (i.e. `email`) are treated as distinct AID sets! You might therefore end up with an AID set such as `[email-1[1]; email-2[1, 2]; email-3[1, 3]]` all referencing the same underlying `email` column, where each AID set might contain partially or fully overlapping AID values (`1` in the example).

The same procedure is followed when joining sensitive and non-sensitive data as well. The only difference being that the non-sensitive data have empty AID sets.


#### Why it is so

The combined larger row resulting from the join must represent all the entities whose data is part of the row.

Two alternatives to the process as described above would be to:

1. let the AID set for a column be the union of the the AID sets for the particular column (`[email[1, 2, 3]]` when continuing the previous example), or
2. letting each half of the JOIN retain their previous AID sets and treating them separately

Each of these alternatives opens up a different variant of the "wide then narrow" attack.

The first attack variant would look like this:

```sql
SELECT l.ssn
FROM (
  SELECT left.ssn -- victim
  FROM left
) l JOIN (
  SELECT other, columns, ...
  FROM right
) r ON l.X = r.X
GROUP BY l.ssn
```

In this attack case a low count social security number would pass the low count threshold through having been joined with other rows and thereby causing the AID set to be inflated.

The other variant, where AID types are kept separate per half of a joined relation might look like this:

```sql
SELECT r.whateverProperty, count(r.*)
FROM (
  SELECT *
  FROM left
) l INNER JOIN (
  SELECT other, columns, ...
  FROM right
) r ON l.X = r.X
GROUP BY l.ssn
```

In this case the left table contains the victim and is, while not selected in the final query, used to influence which rows from the right table are included. The right table values might all pass low count filter, but still offer a signal to the presence or absence of a victim attribute.


### Aggregating rows

When aggregating rows the resulting aggregate has AID sets that are the union of the AID sets of the rows being aggregated. Each AID set is taken the union of independently, that is to say that a row with two AID sets of kind `email` (say `email-1` and `email-2` resulting from a relation having been joined with itself) after the aggregation has an `email-1` AID set that is the union of all the `email-1` AID sets of the aggregated rows, and an `email-2` AID set that is the union of all the `email-2` AID sets of the aggregated rows.

Let's make this more concrete with an example. The query we want to handle
is the following nested query with multiple levels of aggregation:

```sql
SELECT cnt2, count(*) as cnt3
FROM (
  SELECT cnt1, count(*) as cnt2
  FROM (
    SELECT card_type, count(amount) as cnt1
    FROM (... some joined transaction tables ...)
    GROUP BY card_type
  )
  GROUP BY cnt1
) t
GROUP BY cnt2
```

in this query:
- `cnt1` is the number of non null amount entries from the transaction table per `card_type`
- `cnt2` is how many card types have a certain count
- `cnt3` is how many instances exist per `cnt2`

There are additional considerations when aggregating rows (like outlier suppression), but
we gloss over these for the purposes of showing how AIDs are handled.

The input rows to the innermost query might have looked like the following table

| card type | AID sets                       |
| --------- | ------------------------------ |
| standard  | [customer-1[1]; customer-2[1]] |
| standard  | [customer-1[1]; customer-2[1]] |
| standard  | [customer-1[1]; customer-2[3]] |
| standard  | [customer-1[1]; customer-2[1]] |
| standard  | [customer-1[2]; customer-2[1]] |
| standard  | [customer-1[2]; customer-2[1]] |
| standard  | [customer-1[3]; customer-2[1]] |
| platinum  | [customer-1[1]; customer-2[1]] |
| platinum  | [customer-1[4]; customer-2[1]] |
| platinum  | [customer-1[4]; customer-2[1]] |
...
| platinum  | [customer-1[4]; customer-2[1]] |
| platinum  | [customer-1[4]; customer-2[2]] |
| diamond   | [customer-1[4]; customer-2[1]] |
...
| diamond   | [customer-1[4]; customer-2[4]] |
| diamond   | [customer-1[5]; customer-2[6]] |


After one round of aggregation we are left with:

| card_type | AID sets                                | cnt1 |
| --------- | --------------------------------------- | ---- |
| standard  | [customer-1[1, 2, 3]; customer-2[1, 3]] | 5    |
| platinum  | [customer-1[1, 4]; customer-2[1, 2]]    | 2    |
| diamond   | [customer-1[4, 5]; customer-2[1, 4, 6]] | 2    |

To derive `cnt2` we repeat the same procedure, taking the union of AID sets across the rows being joined.

The resulting table becomes:

| cnt1 | AID sets                                      | cnt2 |
| ---- | --------------------------------------------- | ---- |
| 5    | [customer-1[1, 2, 3]; customer-2[1, 3]]       | 1    |
| 2    | [customer-1[1, 4, 5]; customer-2[1, 2, 4, 6]] | 2    |

To derive `cnt3` we repeat the same procedure yet once more, resulting in the following table:

| cnt2 | AID sets                                      | cnt3 |
| ---- | --------------------------------------------- | ---- |
| 1    | [customer-1[1, 2, 3]; customer-2[1, 3]]       | 1    |
| 2    | [customer-1[1, 4, 5]; customer-2[1, 2, 4, 6]] | 1    |

Noise is added as normal.

Also note (for completeness), that if there was one more round of aggregation, the resulting table would be:

| cnt3 | contribution                                           | cnt4 |
| ---- | ------------------------------------------------------ | ---- |
| 1    | [customer-1[1, 2, 3, 4, 5]; customer-2[1, 2, 3, 4, 6]] | 1    |


## Computing noise

When computing noise, the noise amount (standard deviation) is computed separately for each AID set, and the largest such standard deviation value is used. (Note that for `sum()`, for instance, the standard deviation value is taken from the average contribution of all AIDs, or 1/2 the average of the top group. This is how AID affects noise amount.)

When computing flattening (for `sum()`), a flattening value for each distinct AID column is computed separately, and the output is adjusted (flattened) by whichever value is greater. Note that it is possible for the AID columns used for noise and for flattening to be different.

For each new aggregate function, we'll need to take into account multiple AID columns when determining how to perturb the output.

Note that if two AID columns are identical (have the same values and refer to the same entities), then even if they are treated as distinct the right thing happens from an anonymity point of view. For instance, regarding LCF, a bucket might have AID1.1, AID1.2, AID2.1, and AID2.2. It so happens that `AID1.1=AID2.1` and `AID1.2=AID2.2`, but the mechanism doesn't care. The number of distinct AIDs to be used for LCF is two.

The same is the case for noise or flattening. If two AID columns are identical, then the same rows will be used for the top group for both AIDs or for computing the contribution of the AIDs.

Of course, if the AID columns are identical and they are treated as distinct, then the execution is less efficient because Diffix is in essence doing the same work twice. (Optimizations that deal with this are discussed later.)

In any event, the fact that the mechanism doesn't care if two AIDs are identical or different means that in general it isn't necessary for an analyst to JOIN tables simply for the purpose of ensuring that there is an AID. It also means that we can be much more relaxed about what constitutes an AID. In proxy Diffix, not only was there one AID, but that AID had to come from the same identifier space. This caused real difficulties in cases where more than one type of identifier was "AID-like" (i.e. an account number and a customer number). With a more flexible approach, an analyst can label what the "best" columns are to protect each kind of AID, and the mechanism will do the right thing in each case.

Let's look at some examples of this. Suppose we have three tables, `users`, `accounts`, and `atm`.

- `users` has columns: `ssn`, `age`, `gender`, `zip`, where `ssn` is the social security number.
- `accounts` has columns: `ssn`, `account`.
- `atm` has columns `amount`, `account`, where `amount` is the amount withdrawn from the atm.

Some accounts have two users, and some users have multiple accounts. `accounts` has one row per user per account.

Suppose that the administrator tags the following as being AID columns: `atm.account`, `accounts.ssn`, and `users.ssn`. (Strictly speaking the admin should tag `accounts.account` as well, but let's suppose that he or she doesn't.)

If the analyst queries just the `atm` table, then `atm.account` is protected. In some theoretical sense user is not protected, but it would be very hard for an attacker to exploit this. It might be that one user has four accounts, and that user has very high withdrawals in all those accounts, and so that user isn't adequately flattened, but this is a corner case that I wouldn't worry about in practice.

In any event, if the attacker wants to somehow exploit information about that user, then he has to JOIN with `users` and `accounts`, at which point `ssn` is also brought in as an AID, and so now the user is protected.

> Note that the noise depends on what gets JOINed. Not sure we can or need to do anything about this.

Suppose now that the attacker does a query with `atm JOIN users on amount = age`. Such a query would totally mix up `ssn` and `account` in that individual rows would contain `ssn` and `account` that have nothing to do with each other. Nevertheless, both `ssn` and `account` would be protected by virtue of the two AIDs.

### Problem Case: Proxy AIDs

A difficult case is where a column that is not protected (and cannot be tagged as AID) has a high correlation with something that is protected. An example is this:

Three tables, `paychecks`, `users`, and `companies`. We want to protect both users and companies.

- `paychecks`: `user_id`, `amount`, `date`
- `users`: **`user_id`**, `age`, `zip`
- `companies`: `user_id`, `company`

Since `company` only appears in one place, it is tagged there. However, suppose that there are some zips with only one company (but other zips that have multiple companies).

In this case, an attack on a company could be like

```
SELECT sum(p.amount)
FROM user u JOIN paychecks p
ON u.user_id = p.user_id
WHERE u.zip = 12345
```

Since table `companies` is not in the query, `company` is not protected and `zip` acts as a proxy for `company`. I don't think there is a simple solution to this problem. Rather, I think one needs to engineer `company` into the table `users`. One way might be to build a view like:

```
CREATE VIEW `users2` AS
SELECT u.user_id AS user_id, ... c.company
FROM users u JOIN companies c
ON u.user_id = c.user_id
```

The problem with this is that if there are multiple companies per user, then we end up with multiple rows per user in the `users2` table, which might not be what we want.

I'm not sure how big of a problem this will be in practice. Even this example is somewhat contrived, because if a user belongs to multiple companies, then presumably the `paychecks` table would have a column indicating which company the paycheck came from.

### Pseudo-AIDs (or multi-column AIDs)

The clinic database had a problem whereby a person would have multiple `patient_id`. It appeared just to be sloppy registration practice. This would for instance allow someone to view individual names in the database. One way to deal with this might be to allow some kind of multi-column AID, where several columns serve as a pseudo-identifier and Diffix concats them into a single AID. This is risky though, because if the admin uses too many columns, it could have the effect of making one person look like multiple people in the system and then they become attackable in other ways.

Perhaps the better alternative is just to continue to have non-selectable columns.

Or, this might then be better solved by specifying multiple columns as separate AID columns instead.

Say you could have:
- `SSN`
- `patient_id`
- `insurance_id`
- `phone_number`

all being classified as AIDs. The hope would then be that at least one of them is the same for a patient that was given multiple `patient_id`s in a system. Thereby at least low count filtering will work.

### Configuration

From the admin point of view, I think the steps to configuration is something like this:

1. Decide what entities will be protected (individual humans, devices, companies, etc.)
2. For each entity, try to find the single best column in each table that identifies that entity.
3. Take care to note that multiple different people can appear in different columns (sender/receiver).
4. Consider whether the identified columns are 100% effective in identifying the entity, or rather have some flaws (sometimes the same person has multiple AID values, sometime the same AID value has multiple persons). In these cases it may be necessary to use multiple columns for the same entity, or to assign multiple more-or-less redundant AIDs.
5. Take care not to tag redundant columns as AID (i.e. two columns that effectively identify the same entities). Doing so just slows down execution time.
6. Tag each column as AID (note not necessary to configure what entity is being protected, or how AIDs relate to each other).

> TODO: Make sure we include different AIDs having different LCF parameters.

> NOTE(Sebastian): I don't think #5 (not tagging redundant AIDs) should be a goal. In fact tagging all possible AID columns could be the conservative safe thing to do. It certainly adds runtime overhead but helps protect against dirty data (same individual under multiple IDs). I think we should measure the performance impact before making a recommendation against tagging multiple AIDs.

## sum()

If the aggregate is `sum()`, we need to determine which AIDs contribute the most, both for flattening and for determining the amount of noise. I'm assuming that, in the DB, aggregates are often computed on the fly. So for instance, `sum()` is computed by adding the column value to the aggregate as rows are determined to be included.

Since we don't know how much each AID contributes until after query execution, we need to keep a data structure for all AIDs, and compute the contribution for each AID on the fly.

I'm envisioning a hash structure for maintaining AID contributions. There must be good support for this in the DB. For `sum()`, we can add the column value in each row to the AID entry in the hash structure.

If LED requires that we remove a row that was previously added, we can subtract the column value from the hashed AID entry.

| goal             | heavy contributors (top 4-5 AIDs)    |
| ---------------- | ------------------------------------ |
| data structure 1 | hash indexed by AID                  |
| data structure 2 | top few contributing AIDs            |
| insert           | add column value to AID index        |
| delete           | subtract column value from AID index |

## median()

With median, we want to be able to efficiently find the values (and corresponding AIDs) above and below the true median. The idea here is that we don't want to report the true median unless there are enough other AIDs with the same value. If there are not, then we will need to generate a median from some composite (more or less as we did in an earlier version of Aircloak).

One way to compute median efficiently is with a pair of heaps, where the left heap and the right heap have the same number of entries, and the values in the left heap are smaller than the values in the right heap.

| goal             | AIDs above and below true median                                   |
| ---------------- | ------------------------------------------------------------------ |
| data structure 1 | pair of heaps? (see what DB does?)                                 |
| data structure 2 | AIDs above and below                                               |
| insert           | add to heap                                                        |
| delete           | delete from heap (note not needed by native DB median computation) |

## max(), min()

For `max()`, we need to know several AIDs of top values (and bottom values for `min()`).

##

zzzz

