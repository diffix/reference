# Boolean evaluation in Postgres

## Setup

For these examples we will use table `purchases` which has the following shape:

```sql
CREATE TABLE purchases (
  uid INTEGER,
  product TEXT,
  brand TEXT,
  price DOUBLE PRECISION
)
```

## Notes

In boolean steps (AND/OR), `short_circuit_to` refers to the jump that will be done in case
the condition short circuits, i.e. `FALSE` for `AND`s; `TRUE` for `ORs`.

A `QUAL` step is almost identical to an `AND` step. It exists for performance optimization purposes.

## Examples

All queries are of shape:

```sql
SELECT *
FROM purchases
WHERE <condition>
```

Therefore we show only the `<condition>` part.

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
  4: FUNCEXPR
  5: FUNCEXPR_STRICT     texteq
  6: BOOL_OR_STEP_FIRST  short_circuit_to 10
  7: SCAN_VAR            price
  8: FUNCEXPR_STRICT     float8gt
  9: BOOL_OR_STEP_LAST
 10: QUAL (AND step)     short_circuit_to 11
 11: DONE
```

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
