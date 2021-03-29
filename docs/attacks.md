# Attacks

Contents:
- [Attacks](#attacks)
  - [Wide then narrow](#wide-then-narrow)
    - [AID sets become the UNION of their constituent parts](#aid-sets-become-the-union-of-their-constituent-parts)
    - [AID sets are column value properties and do not mix](#aid-sets-are-column-value-properties-and-do-not-mix)

## Wide then narrow

Wide then narrow is an attack whereby an attack writes a query that in a subquery joins a table with the data of a victim to a table that is harmless. Only harmless seeming values are then selected at the top-most anonymizing query, but, if the anonymization system doesn't work right these values are clear indications of whether a property holds of an individual or not.

The [multiple-aid](multiple-aid.md) document describes how AID sets from two queries or relations are merged during a join.
To explain why the design is the way it is, let's consider two other ways in which AID sets could be joined, and how these open for a "wide then narrow" attack.

Alternative handling of AID:
1. AID sets become the UNION of their constituent parts
2. AID sets are column value properties and do not mix

### AID sets become the UNION of their constituent parts

Say we are joining two instances of the same table: `patients`. The `ssn` has been labelled as the AID column.
When joining the patients table with itself you might have two rows with the AIDs `ssn[alice]` and `ssn[bob]`.
This composite row now has data of both the individuals, and one might want to describe it as being associated with
`ssn[alice, bob]`.

To see how this creates a "wide then narrow" attack vector, consider the following query:

```sql
SELECT left.*
FROM (
  SELECT *
  FROM patients
) left, (
  SELECT city, count(*)
  FROM patients
  GROUP BY city
) right
```

In the attack query above, we are joining a relation with per patients data (`left`) with one containing aggregates across multiple users (`right`).
Each bucket on the `right` side might have an AID set such as `ssn[bob, cynthia, dorothea, ferdinand]` whereas the rows on the `left` side have AID sets such as `ssn[alice]`. If the resulting AID sets in the top-level query, after the join, were the combination of the AID sets (`ssn[alice, bob, cynthia, dorothea, ferdinand]`) then absolutely all values from the patients table would pass low count filtering, and the full `patients` table could be read out.


### AID sets are column value properties and do not mix

Say we are, again, joining two instances of the same `patients` table. This time, to avoid the attack described above, we follow an approach whereby the AID values are a property of a column value itself. That is to say, the column values from the `left` subquery only have the AIDs associated with the individual rows of the `patients` table, and the column values from the `right` subquery from the `right` subquery have the AID sets resulting from the aggregation, but not those from the `left` subquery. This would prevent the previous attack that would allow an attacker to read out the `patients` table in it's entirety, but would still allow a slightly more complex variant of the same attack.

```sql
SELECT right.victimExistsIfPresent
FROM (
  SELECT city
  FROM patients
  WHERE name = "Alice" and hasCancer and smokesTooMuch
) left INNER JOIN (
  SELECT city, count(*) victimExistsIfPresent
  FROM patients
  GROUP BY city
) right ON left.city = right.city
```

In this case we are only reporting values from the `right` subquery in the anonymizing query. Each of these values are likely to pass low count filter as a result of being aggregates. These aggregates however only make it out of the join if the victim exists in the `left` subquery and the victim has a certain set of attributes we want to learn.

Through this attack we can learn any property of any user through asking a series of yes/no queries. Yes being indicated through there being a result returned, and no being indicated by the result being empty.