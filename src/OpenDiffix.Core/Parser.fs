﻿module OpenDiffix.Core.Parser

open ParserTypes
open type System.Char

module QueryParser =
  open FParsec

  let opp = OperatorPrecedenceParser<Expression, unit, unit>()

  let expr = opp.ExpressionParser

  let simpleIdentifier =
    let isIdentifierFirstChar token = isLetter token

    let isIdentifierChar token =
      isLetter token || isDigit token || token = '_'

    many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier"

  let quotedIdentifier =
    let isIdentifierChar token = token <> '"' && token <> '\n'

    pchar '"' >>. many1SatisfyL isIdentifierChar "quoted identifier" .>> pchar '"'

  let identifier = (simpleIdentifier <|> quotedIdentifier) .>> spaces

  let qualifiedIdentifier =
    identifier .>>. opt (pchar '.' >>. identifier)
    |>> function
      | name, None -> Expression.Identifier(None, name)
      | name1, Some name2 -> Expression.Identifier(Some name1, name2)

  let word word = pstringCI word .>> spaces

  let words words =
    words |> List.map word |> List.reduce (>>.)

  let between c1 c2 p =
    pchar c1 >>. spaces >>. p .>> pchar c2 .>> spaces

  let inParenthesis p = between '(' ')' p

  let commaSeparated p = sepBy1 p (pchar ',' .>> spaces)

  let star = word "*" |>> fun _ -> Expression.Star
  let asc = word "ASC" |>> fun _ -> Expression.Asc
  let desc = word "DESC" |>> fun _ -> Expression.Desc
  let nullsFirst = word "NULLS FIRST" |>> fun _ -> Expression.NullsFirst
  let nullsLast = word "NULLS LAST" |>> fun _ -> Expression.NullsLast

  let alias = word "AS" >>. identifier

  let numberFormat =
    NumberLiteralOptions.AllowMinusSign
    ||| NumberLiteralOptions.AllowFraction
    ||| NumberLiteralOptions.AllowExponent

  let number =
    numberLiteral numberFormat "number" .>> spaces
    |>> fun nl ->
          if nl.IsInteger then
            Expression.Integer(int64 nl.String)
          else
            Expression.Float(float nl.String)

  let boolean =
    (word "true" |>> fun _ -> Expression.Boolean true)
    <|> (word "false" |>> fun _ -> Expression.Boolean false)

  let stringLiteral =
    skipChar '\'' >>. manySatisfy (fun c -> c <> '\'') .>> skipChar '\'' .>> spaces
    |>> Expression.String

  let spaceSepUnaliasedExpressions = many1 expr

  let functionExpression =
    simpleIdentifier .>> spaces .>>. inParenthesis (commaSeparated expr) .>> spaces
    |>> fun (funName, exprs) -> Function(funName.ToLower(), exprs)

  let typeName =
    word "text"
    <|> word "integer"
    <|> word "real"
    <|> word "boolean"
    <|> word "timestamp"

  let datePartName =
    word "seconds"
    <|> word "second"
    <|> word "epoch"
    <|> word "minutes"
    <|> word "minute"
    <|> word "hours"
    <|> word "hour"
    <|> word "days"
    <|> word "day"
    <|> word "dow"
    <|> word "doy"
    <|> word "isodow"
    <|> word "weeks"
    <|> word "week"
    <|> word "months"
    <|> word "month"
    <|> word "quarter"
    <|> word "years"
    <|> word "year"
    <|> word "isoyear"
    <|> word "decades"
    <|> word "decade"
    <|> word "century"
    <|> word "centuries"
    <|> word "millenniums"
    <|> word "millennium"
    <|> word "millennia"

  let castExpression =
    word "cast" >>. inParenthesis (expr .>> word "as" .>>. typeName) .>> spaces
    |>> fun (expr, typeName) -> Function("cast", [ expr; String typeName ])

  let extractExpression =
    word "extract" >>. inParenthesis (datePartName .>> word "from" .>>. expr)
    .>> spaces
    |>> fun (datePartName, expr) -> Function("extract", [ String datePartName; expr ])

  let selectedExpression = expr .>>. opt alias |>> As

  let commaSepExpressions = commaSeparated (star <|> selectedExpression) .>> spaces

  let whereClause = word "WHERE" >>. expr

  let havingClause = word "HAVING" >>. expr

  let limitClause = word "LIMIT" >>. puint32

  let orderSpec =
    expr .>>. opt (asc <|> desc) .>>. opt (nullsFirst <|> nullsLast) .>> spaces
    |>> (fun ((expr, optDirection), optNullsBehavior) ->

      let (direction, nullsBehavior) =
        // we want the default nulls behavior to be "NULL values are largest", `ORDER BY x DESC` is a special case
        match (optDirection, optNullsBehavior) with
        | None, None -> Asc, NullsLast
        | Some Asc, None -> Asc, NullsLast
        | Some Desc, None -> Desc, NullsFirst
        | None, Some nullsBehavior -> Asc, nullsBehavior
        | Some direction, Some nullsBehavior -> direction, nullsBehavior
        | _ -> failwith "Invalid `ORDER BY` clause"

      OrderSpec(expr, direction, nullsBehavior)
    )

  let orderBy = words [ "ORDER"; "BY" ] .>> spaces >>. commaSeparated orderSpec

  let groupBy = words [ "GROUP"; "BY" ] .>> spaces >>. commaSeparated expr

  let distinct = opt (word "distinct") |>> Option.isSome

  let table = identifier .>>. opt alias |>> Expression.Table

  let joinType =
    choice
      [
        word "JOIN" >>. preturn InnerJoin
        words [ "INNER"; "JOIN" ] >>. preturn InnerJoin
        words [ "LEFT"; "JOIN" ] >>. preturn LeftJoin
        words [ "RIGHT"; "JOIN" ] >>. preturn RightJoin
        words [ "FULL"; "JOIN" ] >>. preturn FullJoin
      ]

  let selectQuery, selectQueryRef = createParserForwardedToRef<Expression, unit> ()

  let subQuery =
    inParenthesis selectQuery .>>. identifier
    >>= function
      | SelectQuery subQuery, alias -> preturn <| SubQuery(subQuery, alias)
      | _ -> fail "Expected sub-query"

  let tableOrSubQuery = attempt subQuery <|> table

  let regularJoin = joinType .>>. tableOrSubQuery .>> word "on" .>>. expr

  let crossJoin =
    word "," >>. tableOrSubQuery
    |>> fun tableOrSubQuery -> (InnerJoin, tableOrSubQuery), Boolean true

  let join = crossJoin <|> regularJoin

  let from =
    word "FROM" >>. tableOrSubQuery .>>. many join
    |>> fun (first_table, joined_tables) ->
          List.fold (fun left ((joinType, right), on) -> Join(joinType, left, right, on)) first_table joined_tables

  do
    selectQueryRef.Value <-
      word "SELECT"
      >>= fun _ ->
            distinct
            >>= fun distinct ->
                  commaSepExpressions
                  >>= fun columns ->
                        from
                        >>= fun from ->
                              opt whereClause
                              >>= fun whereClause ->
                                    opt groupBy
                                    >>= fun groupBy ->
                                          opt havingClause
                                          >>= fun having ->
                                                opt orderBy
                                                >>= fun orderBy ->
                                                      opt limitClause
                                                      >>= fun limit ->
                                                            let query =
                                                              {
                                                                SelectDistinct = distinct
                                                                Expressions = columns
                                                                From = from
                                                                Where = whereClause
                                                                GroupBy = groupBy |> Option.defaultValue []
                                                                Having = having
                                                                Limit = limit
                                                                OrderBy = orderBy |> Option.defaultValue []
                                                              }

                                                            preturn (Expression.SelectQuery query)

  // This is sort of silly... but the operator precedence parser is case sensitive. This means
  // if we add a parser for AND, then it will fail if you write a query as And... Therefore
  // this function brute forces all cases of a word...
  let allCasingPermutations (s: string) =
    let rec createPermutations acc next =
      match acc, next with
      | acc, c :: cs ->
        let newAcc =
          acc
          |> List.collect (fun prefix -> //
            List.distinct [ $"%s{prefix}%c{ToLower c}"; $"%s{prefix}%c{ToUpper c}" ]
          )

        createPermutations newAcc cs
      | acc, [] -> acc

    s.ToCharArray() |> Array.toList |> createPermutations [ "" ]

  let addOperator opType opName parseNext precedence associativity f =
    allCasingPermutations opName
    |> List.iter (fun opVariant -> opp.AddOperator(opType (opVariant, parseNext, precedence, associativity, f)))

  let addInfixOperator = addOperator InfixOperator
  let addPrefixOperator = addOperator PrefixOperator
  let addPostfixOperator = addOperator PostfixOperator

  addPrefixOperator "distinct" spaces 1 false Expression.Distinct
  addInfixOperator "and" spaces 1 Associativity.Left (fun left right -> Expression.And(left, right))

  addInfixOperator "or" (notFollowedBy (word "der by") .>> spaces) 2 Associativity.Left
  <| (fun left right -> Expression.Or(left, right))

  addPrefixOperator "not" spaces 2 false Expression.Not

  addInfixOperator "=" spaces 3 Associativity.Left (fun left right -> Expression.Equals(left, right))
  addInfixOperator "<>" spaces 3 Associativity.Left (fun left right -> Expression.Not(Expression.Equals(left, right)))
  addInfixOperator ">" spaces 3 Associativity.Left (fun left right -> Expression.Gt(left, right))
  addInfixOperator "<" spaces 3 Associativity.Left (fun left right -> Expression.Lt(left, right))
  addInfixOperator "<=" spaces 3 Associativity.Left (fun left right -> Expression.LtE(left, right))
  addInfixOperator ">=" spaces 3 Associativity.Left (fun left right -> Expression.GtE(left, right))
  addInfixOperator "+" spaces 4 Associativity.Left (fun left right -> Expression.Function("+", [ left; right ]))
  addInfixOperator "-" spaces 4 Associativity.Left (fun left right -> Expression.Function("-", [ left; right ]))
  addInfixOperator "*" spaces 5 Associativity.Left (fun left right -> Expression.Function("*", [ left; right ]))
  addInfixOperator "/" spaces 5 Associativity.Left (fun left right -> Expression.Function("/", [ left; right ]))
  addInfixOperator "%" spaces 6 Associativity.Left (fun left right -> Expression.Function("%", [ left; right ]))
  addInfixOperator "||" spaces 7 Associativity.Left (fun left right -> Expression.Function("||", [ left; right ]))
  addPostfixOperator "is null" spaces 8 false Expression.IsNull
  addPostfixOperator "is not null" spaces 8 false (Expression.IsNull >> Expression.Not)

  opp.TermParser <-
    choice
      [
        (attempt selectQuery)
        (attempt castExpression)
        (attempt extractExpression)
        (attempt functionExpression)
        inParenthesis expr
        star
        number
        boolean
        stringLiteral
        qualifiedIdentifier
      ]

  let fullParser = spaces >>. selectQuery .>> (opt (pchar ';')) .>> spaces .>> eof

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let parse sql : SelectQuery =
  match FParsec.CharParsers.run QueryParser.fullParser sql with
  | FParsec.CharParsers.Success(result, _, _) ->
    match result with
    | SelectQuery selectQuery -> selectQuery
    | _ -> failwith "Parse error: Expecting SELECT query"
  | FParsec.CharParsers.Failure(errorMessage, _, _) -> failwith ("Parse error: " + errorMessage)
