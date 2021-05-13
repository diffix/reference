module rec OpenDiffix.Core.Expression

// ----------------------------------------------------------------
// Type resolution
// ----------------------------------------------------------------

/// Resolves the common type from a list expression.
let typeOfList expressions =
  expressions |> List.map typeOf |> ExpressionType.commonType

/// Resolves the type of a scalar function expression.
let typeOfScalarFunction fn args =
  match fn with
  | Add
  | Subtract ->
      args
      |> List.map typeOf
      |> function
      | [ IntegerType; IntegerType ] -> IntegerType
      | _ -> RealType
  | Not
  | And
  | Equals
  | IsNull
  | Or
  | Lt
  | LtE
  | Gt
  | GtE -> BooleanType
  | Length -> IntegerType

/// Resolves the type of a set function expression.
let typeOfSetFunction fn _args =
  match fn with
  | GenerateSeries -> IntegerType

/// Resolves the type of an aggregate function expression.
let typeOfAggregate fn args =
  match fn with
  | Count
  | DiffixCount -> IntegerType
  | DiffixLowCount -> BooleanType
  | Sum ->
      match typeOfList args with
      | ListType IntegerType -> IntegerType
      | _ -> RealType
  | MergeAids -> ListType MIXED_TYPE

/// Resolves the type of an expression.
let rec typeOf expression =
  match expression with
  | FunctionExpr (ScalarFunction fn, args) -> typeOfScalarFunction fn args
  | FunctionExpr (SetFunction fn, args) -> typeOfSetFunction fn args
  | FunctionExpr (AggregateFunction (fn, _options), args) -> typeOfAggregate fn args
  | ColumnReference (_, exprType) -> exprType
  | Constant c -> Value.typeOf c
  | ListExpr expressions -> typeOfList expressions

// ----------------------------------------------------------------
// Evaluation
// ----------------------------------------------------------------

/// Evaluates the result of a scalar function invocation.
let rec evaluateScalarFunction fn args =
  match fn, args with
  | And, [ Boolean false; _ ] -> Boolean false
  | And, [ _; Boolean false ] -> Boolean false
  | Or, [ Boolean true; _ ] -> Boolean true
  | Or, [ _; Boolean true ] -> Boolean true

  | IsNull, [ v1 ] -> Boolean(v1 = Null)

  // From now on, if the unary or binary function gets a `Null` argument, we return `Null` directly.
  | _, [ Null ] -> Null
  | _, [ Null; _ ] -> Null
  | _, [ _; Null ] -> Null

  | Not, [ Boolean b ] -> Boolean(not b)
  | And, [ Boolean b1; Boolean b2 ] -> Boolean(b1 && b2)
  | Or, [ Boolean b1; Boolean b2 ] -> Boolean(b1 || b2)

  | _, [ Integer i; Real r ] -> evaluateScalarFunction fn [ Real(double i); Real r ]
  | _, [ Real r; Integer i ] -> evaluateScalarFunction fn [ Real r; Real(double i) ]

  | Add, [ Integer i1; Integer i2 ] -> Integer(i1 + i2)
  | Add, [ Real r1; Real r2 ] -> Real(r1 + r2)
  | Subtract, [ Integer i1; Integer i2 ] -> Integer(i1 - i2)
  | Subtract, [ Real r1; Real r2 ] -> Real(r1 - r2)

  | Equals, [ v1; v2 ] -> Boolean(v1 = v2)

  | Lt, [ v1; v2 ] -> Boolean(v1 < v2)
  | LtE, [ v1; v2 ] -> Boolean(v1 <= v2)
  | Gt, [ v1; v2 ] -> Boolean(v1 > v2)
  | GtE, [ v1; v2 ] -> Boolean(v1 >= v2)

  | Length, [ String s ] -> Integer(int64 s.Length)

  | _ -> failwith $"Invalid usage of scalar function '%A{fn}'."

/// Evaluates the result sequence of a set function invocation.
let evaluateSetFunction fn args =
  match fn, args with
  | GenerateSeries, [ Integer count ] -> seq { for i in 1L .. count -> Integer i }
  | _ -> failwith $"Invalid usage of set function '%A{fn}'."

/// Evaluates the expression for a given row.
let rec evaluate (ctx: EvaluationContext) (row: Row) (expr: Expression) =
  match expr with
  | FunctionExpr (ScalarFunction fn, args) -> evaluateScalarFunction fn (args |> List.map (evaluate ctx row))
  | FunctionExpr (AggregateFunction (fn, _options), _) -> failwith $"Invalid usage of aggregate function '%A{fn}'."
  | FunctionExpr (SetFunction fn, _) -> failwith $"Invalid usage of set function '%A{fn}'."
  | ListExpr expressions -> expressions |> List.map (evaluate ctx row) |> Value.List
  | ColumnReference (index, _) -> row.[index]
  | Constant value -> value

/// Sorts a row sequence based on the given orderings.
let sortRows ctx orderings (rows: Row seq) =
  let rec performSort orderings rows =
    match orderings with
    | [] -> rows
    | OrderBy (expr, direction, nulls) :: tail ->
        let compare = Value.comparer direction nulls

        rows
        |> Seq.sortWith (fun rowA rowB ->
          let valueA = evaluate ctx rowA expr
          let valueB = evaluate ctx rowB expr
          compare valueA valueB
        )
        |> performSort tail

  performSort (List.rev orderings) rows

// ----------------------------------------------------------------
// Utils
// ----------------------------------------------------------------

let rec isScalar expr =
  match expr with
  | FunctionExpr (AggregateFunction _, _) -> false
  | FunctionExpr (_, args) -> List.forall isScalar args
  | _ -> true
