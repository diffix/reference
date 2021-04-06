# Expression evaluation in Postgres

In this document we show how expessions of conditions are evaluated during query execution.

**Table of Contents**

- [Expression evaluation in Postgres](#expression-evaluation-in-postgres)
- [Setup](#setup)
- [Notes](#notes)
- [Examples](#examples)
  - [Basic operators (AND/OR/NOT)](#basic-operators-andornot)
  - [Functions and math](#functions-and-math)
  - [Multiple columns](#multiple-columns)
  - [Subqueries](#subqueries)
- [OpCode Reference](#opcode-reference)

# Setup

For these examples we will use table `purchases` which has the following shape:

```sql
CREATE TABLE purchases (
  uid INTEGER,
  product TEXT,
  brand TEXT,
  price DOUBLE PRECISION
)
```

# Notes

In boolean steps (AND/OR), `short_circuit_to` refers to the jump that will be done in case
the condition short circuits, i.e. `FALSE` for `AND`s; `TRUE` for `ORs`.

A `QUAL` step is almost identical to an `AND` step. It exists for performance optimization purposes.

When queries are of the following shape, we show the `<condition>` part only.

```sql
SELECT *
FROM purchases
WHERE <condition>
```

# Examples

## Basic operators (AND/OR/NOT)

```
uid = 1

  1: SCAN_FETCHSOME      1
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4eq
  4: QUAL (AND step)     short_circuit_to 5
  5: DONE
```

```
uid = 1 AND product = 'phone'

  1: SCAN_FETCHSOME      2
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4eq
  4: QUAL (AND step)     short_circuit_to 8
  5: SCAN_VAR            product
  6: FUNCEXPR_STRICT     texteq
  7: QUAL (AND step)     short_circuit_to 8
  8: DONE
```

```
uid = 1 OR product = 'phone'

  1: SCAN_FETCHSOME      2
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4eq
  4: BOOL_OR_STEP_FIRST  short_circuit_to 8
  5: SCAN_VAR            product
  6: FUNCEXPR_STRICT     texteq
  7: BOOL_OR_STEP_LAST
  8: QUAL (AND step)     short_circuit_to 9
  9: DONE
```

```
(product = 'phone' AND uid = 1) OR price > 100

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            product
  3: FUNCEXPR_STRICT     texteq
  4: BOOL_AND_STEP_FIRST short_circuit_to 8
  5: SCAN_VAR            uid
  6: FUNCEXPR_STRICT     int4eq
  7: BOOL_AND_STEP_LAST
  8: BOOL_OR_STEP_FIRST  short_circuit_to 12
  9: SCAN_VAR            price
 10: FUNCEXPR_STRICT     float8gt
 11: BOOL_OR_STEP_LAST
 12: QUAL (AND step)     short_circuit_to 13
 13: DONE
```

```
(product = 'phone' OR uid = 1) AND price > 100

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: FUNCEXPR_STRICT     float8gt
  4: QUAL (AND step)     short_circuit_to 12
  5: SCAN_VAR            product
  6: FUNCEXPR_STRICT     texteq
  7: BOOL_OR_STEP_FIRST  short_circuit_to 11
  8: SCAN_VAR            uid
  9: FUNCEXPR_STRICT     int4eq
 10: BOOL_OR_STEP_LAST
 11: QUAL (AND step)     short_circuit_to 12
 12: DONE
```

Notice how `price > 100` is evaluated first in the 2 previous examples.

```
uid = 1 OR product = 'phone' OR product='laptop' OR price > 100

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4eq
  4: BOOL_OR_STEP_FIRST  short_circuit_to 14
  5: SCAN_VAR            product
  6: FUNCEXPR_STRICT     texteq
  7: BOOL_OR_STEP        short_circuit_to 14
  8: SCAN_VAR            product
  9: FUNCEXPR_STRICT     texteq
 10: BOOL_OR_STEP        short_circuit_to 14
 11: SCAN_VAR            price
 12: FUNCEXPR_STRICT     float8gt
 13: BOOL_OR_STEP_LAST
 14: QUAL (AND step)     short_circuit_to 15
 15: DONE
```

```
NOT (product = 'phone' AND uid = 1)

  1: SCAN_FETCHSOME      2
  2: SCAN_VAR            product
  3: FUNCEXPR_STRICT     textne
  4: BOOL_OR_STEP_FIRST  short_circuit_to 8
  5: SCAN_VAR            uid
  6: FUNCEXPR_STRICT     int4ne
  7: BOOL_OR_STEP_LAST
  8: QUAL (AND step)     short_circuit_to 9
  9: DONE
```

In the example above De Morgan's law has been applied.

```
(product = 'phone' AND uid <> 1) OR (uid = 1 AND price > 100 AND product <> 'phone')

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            product
  3: FUNCEXPR_STRICT     texteq
  4: BOOL_AND_STEP_FIRST short_circuit_to 8
  5: SCAN_VAR            uid
  6: FUNCEXPR_STRICT     int4ne
  7: BOOL_AND_STEP_LAST
  8: BOOL_OR_STEP_FIRST  short_circuit_to 19
  9: SCAN_VAR            uid
 10: FUNCEXPR_STRICT     int4eq
 11: BOOL_AND_STEP_FIRST short_circuit_to 18
 12: SCAN_VAR            price
 13: FUNCEXPR_STRICT     float8gt
 14: BOOL_AND_STEP       short_circuit_to 18
 15: SCAN_VAR            product
 16: FUNCEXPR_STRICT     textne
 17: BOOL_AND_STEP_LAST
 18: BOOL_OR_STEP_LAST
 19: QUAL (AND step)     short_circuit_to 20
 20: DONE
```

## Functions and math

```
product NOT LIKE '%phone%'

  1: SCAN_FETCHSOME      2
  2: SCAN_VAR            product
  3: FUNCEXPR_STRICT     textnlike
  4: QUAL (AND step)     short_circuit_to 5
  5: DONE
```

```
concat(uid, product) = '1phone' OR price > 100

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            uid
  3: SCAN_VAR            product
  4: FUNCEXPR            concat
  5: FUNCEXPR_STRICT     texteq
  6: BOOL_OR_STEP_FIRST  short_circuit_to 10
  7: SCAN_VAR            price
  8: FUNCEXPR_STRICT     float8gt
  9: BOOL_OR_STEP_LAST
 10: QUAL (AND step)     short_circuit_to 11
 11: DONE
```

```
sqrt(uid) = 2

  1: SCAN_FETCHSOME      1
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     float8
  4: FUNCEXPR_STRICT     sqrt
  5: FUNCEXPR_STRICT     float8eq
  6: QUAL (AND step)     short_circuit_to 7
  7: DONE
```

Instruction `3` above casts `uid` to `float8` before calling `sqrt`.
Below that is not necessary because it's already a `float8`.

```
sqrt(price) = 2

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: FUNCEXPR_STRICT     sqrt
  4: FUNCEXPR_STRICT     float8eq
  5: QUAL (AND step)     short_circuit_to 6
  6: DONE
```

```
price - price = 0

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: SCAN_VAR            price
  4: FUNCEXPR_STRICT     float8mi
  5: FUNCEXPR_STRICT     float8eq
  6: QUAL (AND step)     short_circuit 7
  7: DONE
```

```
uid + 10 = 12

----------------------------------------------------------
                        QUERY PLAN
----------------------------------------------------------
 Seq Scan on purchases  (cost=0.00..4.82 rows=1 width=22)
   Filter: ((uid + 10) = 12)
----------------------------------------------------------

  1: SCAN_FETCHSOME      1
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4pl
  4: FUNCEXPR_STRICT     int4eq
  5: QUAL (AND step)     short_circuit_to 6
  6: DONE
```

In the example above, the constants are not folded across sides of the equation.
The `int4pl` instructions means the addition is happening before the equals comparison (`int4eq`).

If we have constant operations in one side of the equality like below,
we see that it folds them during planning.

```
uid = 12 - 10

----------------------------------------------------------
                        QUERY PLAN
----------------------------------------------------------
 Seq Scan on purchases  (cost=0.00..4.35 rows=4 width=22)
   Filter: (uid = 2)
----------------------------------------------------------

  1: SCAN_FETCHSOME      1
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4eq
  4: QUAL (AND step)     short_circuit_to 5
  5: DONE
```

```
round(price * 3.45) = 20

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: FUNCEXPR_STRICT     float8mul
  4: FUNCEXPR_STRICT     round
  5: FUNCEXPR_STRICT     float8eq
  6: QUAL (AND step)     short_circuit_to 7
  7: DONE
```

```
round(price * 3.45) > 100 OR round(uid * 13.25) > 10

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: FUNCEXPR_STRICT     float8mul
  4: FUNCEXPR_STRICT     round
  5: FUNCEXPR_STRICT     float8gt
  6: BOOL_OR_STEP_FIRST  short_circuit_to 13
  7: SCAN_VAR            uid
  8: FUNCEXPR_STRICT     numeric
  9: FUNCEXPR_STRICT     numeric_mul
 10: FUNCEXPR_STRICT     round
 11: FUNCEXPR_STRICT     numeric_gt
 12: BOOL_OR_STEP_LAST
 13: QUAL (AND step)     short_circuit_to 14
 14: DONE
```

## Multiple columns

```
product = brand

  1: SCAN_FETCHSOME      3
  2: SCAN_VAR            product
  3: SCAN_VAR            brand
  4: FUNCEXPR_STRICT     texteq
  5: QUAL (AND step)     short_circuit_to 6
  6: DONE
```

```
round(price) = uid

  1: SCAN_FETCHSOME      4
  2: SCAN_VAR            price
  3: FUNCEXPR_STRICT     round
  4: SCAN_VAR            uid
  5: FUNCEXPR_STRICT     float8
  6: FUNCEXPR_STRICT     float8eq
  7: QUAL (AND step)     short_circuit_to 8
  8: DONE
```

In the example above order of evaluation goes like the following:

```
  1: Prepare tuple for reading.
  2: Get price var, put result in round arg1.
  3: Call round func, put result in float8eq arg1.
  4: Get uid var, put result in float8 arg1.
  5: Call float8 func, put result in float8eq arg2.
  6: Call float8eq func, put result in expression result.
  7: If expr. result is NULL or false, put false in result slot and jump to 8.
  8: Done. Return expression result.
```

## Subqueries

```
SELECT uid, cnt
FROM (
  SELECT uid, count(*) AS cnt
  FROM purchases
  WHERE uid > 10
  GROUP BY uid
) t
WHERE uid < 20

----------------------------------------------------------------
                           QUERY PLAN
----------------------------------------------------------------
 HashAggregate  (cost=5.30..5.39 rows=9 width=12)
   Group Key: purchases.uid
   ->  Seq Scan on purchases  (cost=0.00..4.82 rows=96 width=4)
         Filter: ((uid > 10) AND (uid < 20))
----------------------------------------------------------------

  1: SCAN_FETCHSOME      1
  2: SCAN_VAR            uid
  3: FUNCEXPR_STRICT     int4gt
  4: QUAL (AND step)     short_circuit_to 8
  5: SCAN_VAR            uid
  6: FUNCEXPR_STRICT     int4lt
  7: QUAL (AND step)     short_circuit_to 8
  8: DONE
```

In the example above the planner has been smart enough to push the `uid < 20` condition down to the inner seq scan.

# OpCode Reference

| Operator            | Explanation                                                                                                |
| ------------------- | ---------------------------------------------------------------------------------------------------------- |
| SCAN_FETCHSOME      | Prepares `n` first attributes from the scan tuple for reading.                                             |
| SCAN_VAR            | Retrieves a variable (column) from a scan tuple. Value is stored in some destination.                      |
| FUNCEXPR            | Calls a function. Result is stored in some destination.                                                    |
| FUNCEXPR_STRICT     | Calls a function if all args are non-NULL. If any arg is NULL then NULL is returned directly.              |
| BOOL_OR_STEP        | Evaluates value passed to operation. If `false` or `NULL` do nothing; if `true` jump to `jumpdone`.        |
| BOOL_OR_STEP_FIRST  | Optimized `BOOL_OR_STEP` for first subexpression in `OR` series.                                           |
| BOOL_OR_STEP_LAST   | Optimized `BOOL_OR_STEP` for last subexpression in `OR` series. Has no reason to jump on truthy value.     |
| BOOL_AND_STEP       | Evaluates value passed to operation. If `true` do nothing; if `false` or `NULL` jump to `jumpdone`.        |
| BOOL_AND_STEP_FIRST | Optimized `BOOL_AND_STEP` for first subexpression in `AND` series.                                         |
| BOOL_AND_STEP_LAST  | Optimized `BOOL_AND_STEP` for last subexpression in `AND` series. Has no reason to jump on falsy value.    |
| BOOL_NOT            | Converts `true` to `false`, `false` to `true`, and `NULL` to `NULL`. Result is stored in some destination. |
| QUAL                | Same as AND step, but optimized for performance. Jumps to `DONE` if value is `NULL` or `false`.            |
| DONE                | Evaluation is complete. Return result to caller.                                                           |
