### Noise Layers

This mechanism aims to defend against "difference attacks", whereby the analyst uses the mere fact that two answers are different to conclude something about the database. Noise layers ensure that two queries that would otherwise produce the same answer may well in fact have different answers, so the analyst cannot be sure of the result of their attack.

To achieve this, all noisy values produced are generated using a set of random number generators instead of just one. Each generator uses the same mean and SD and the resulting noise is summed. The generators differ in their seed, and how exactly the seed is built depends on the query the analyst issued. This way, even if two different queries would have the same result, they will have different noise applied, resulting in (potentially) different results.

A noise layer is an individual Gaussian noise sample. The total noise added to an aggregate value in each bucket of a query answer is the sum of the individual noise samples. Although noise layers are taken from a Gaussian distribution, their values are not random. Rather, the pseudo-random number generator (PRNG) used to generate each noise sample is seeded in such a way that identical query conditions generate the same noise sample. We refer to this property as being _sticky_.

Noise layers typically (though not always) depend only on the semantics of the individual SQL query filter condition. In other words, the `WHERE` condition `age = 10` will always produce the same noise layer independent of what AIDs comprise the answer (the exception being when low-effect conditions are present).

Filter condition that apply to an aggregator's input have at least one noise layer. The following are considered to be filter conditions:

- `WHERE` and `HAVING` clauses;
- `GROUP BY` clauses;
- `SELECT` clauses.

Additionally, there is a generic noise layer for queries that otherwise have no noise layer (because they have no filter conditions). The query `SELECT count(*) FROM table` is such a query.

### Determine seeds

Most noise layers are seeded with at least the following:

- A canonical name of the column in the form "table.column";
- A secret salt (random value) that is set in the configuration file;
- The min and max column value.

Negative conditions add the symbol `<>` to the seed material. Note that `NOT IN` conditions are converted to their equivalent `<>` forms.

The generic noise layer is seeded with the names of the tables targeted by the query and the secret salt.

### Clear conditions

The system may require that certain conditions are _clear_. The primary purpose of clear conditions is so that the system can seed noise layers through SQL inspection rather than by column value inspection.

The term "clear" implies that it is clear from SQL inspection alone what the semantics of the conditions are, and therefore how to seed the corresponding noise layers. Clear conditions also have the effect of reducing the attack surface since it gives an attacker fewer mechanisms to work with. 

A column in a clear condition cannot have undergone transformations prior to the condition. For instance, in the following query, the `IN` condition must be clear, but since there is a prior transformation (`numeric + 1` in the sub-query), the condition is unclear and the query rejected.

```sql
SELECT COUNT(*)
FROM (
    SELECT numeric + 1 AS number
    FROM table
) t
WHERE number IN (1, 2, 3)
```
