Please consult the [glossary](glossary.md) for definitions of terms used in this document.

# Multiple AIDs

A table may have one or more AID columns (columns labeled as being AIDs). When there is more than one AID in a query (either because there are multiple AIDs in a table or tables have been joined), by default, Diffix treats them as distinct. In other words, as though they refer to different entities. In so doing, Diffix handles AIDs of different types seamlessly, and also is robust in cases where `JOIN` operations incorrectly mix AIDs together (i.e. a row for user1 is joined with a row for user2).

We use the nomenclature `AIDX[Y1]` to describe that a row has AID column `AIDX` and that it belongs to the entity identified by `Y1`. For example `AIDX` might be `send_email` and `Y1` might be `sue1@gmail.com`, in which case a row might have the AID `send_email[sue1@gmail.com]`.
If the table additionally contains a second AID column `recipient_email` then the same row might be fully described through the pair of AIDs `[send_email[sue1@gmail.com]; recipient_email[bob6@yahoo.com]]`.

If a table is joined with itself, then the AIDs from the left and right side of the join are treated as distinct.
You might therefore end up with `[send_emailL[sue1@gmail.com]; send_emailR[sue1@gmail.com]]` for the same row. While both refer to the same entity, our system still treats them as separate AIDs.

Through aggregation a row might be associated with multiple AID values for a single type of AID. Should we aggregate the aforementioned email table by the day of the week, we might for example end up with an intermediate row for Mondays with an AID set such as `[send_email[sue1@gmail.com, bob6@yahoo.com, liz@hey.com, esmeralda@icloud.com]]` indicating that `sue1`, `bob`, `liz` as well as `esmeralda` sent one or more emails on that day.


## Low count filter

Low count filtering is done per AID type individually. For a bucket with the AID sets `[email1; email2; email3; ssn1; ssn2]` all five AID sets individually need to pass the low count threshold. Each kind of AID set might have a distinct  `minimum_allowed_aids` parameter (i.e. the parameter for `email` might differ from that of `ssn`).


# How AIDs spread

## JOINing rows

When joining rows from two selectables, the combined rows AID sets is the union of the AID sets of the rows being joined. Crucially AID sets of the same kind (i.e. `email`) are treated as distinct AID sets! You might therefore end up with an AID set such as `[email-1[1]; email-2[1, 2]; email-3[1, 3]]` all referencing the same underlying `email` column, where each AID set might contain partially or fully overlapping AID values (`1` in the example).

The same procedure is followed when joining sensitive and non-sensitive data as well. The only difference being that the non-sensitive data have empty AID sets.

For a more in-depth discussion about why AID sets have to be merged, but not joined, have a look at the "Wide and Narrow" section of the [attack](attacks.md) document.


## Aggregating rows

When aggregating rows the resulting aggregate has AID sets that are the union of the AID sets of the rows being aggregated. Each AID set is taken the union of independently, that is to say that a row with two AID sets of kind `email` (say `email-1` and `email-2` resulting from a selectable having been joined with itself) after the aggregation has an `email-1` AID set that is the union of all the `email-1` AID sets of the aggregated rows, and an `email-2` AID set that is the union of all the `email-2` AID sets of the aggregated rows.

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

There are additional considerations when aggregating rows (like extreme value flattening which you can read more about in
[multi-level-aggregation](multi-level-aggregation.md)), but we gloss over these here for the purposes of showing how AIDs are handled.

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