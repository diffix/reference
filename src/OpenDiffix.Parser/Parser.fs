module OpenDiffix.Parser

open OpenDiffix.Core.ParserTypes

module QueryParser =
  open FParsec

  let opp = OperatorPrecedenceParser<Expression, unit, unit>()

  let expr = opp.ExpressionParser

  let simpleIdentifier =
    let isIdentifierFirstChar token = isLetter token

    let isIdentifierChar token =
      isLetter token || isDigit token || token = '_'

    many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier" .>> spaces

  let identifier =
    simpleIdentifier .>>. opt (pchar '.' >>. simpleIdentifier)
    |>> function
    | name, None -> Expression.Identifier(None, name)
    | name1, Some name2 -> Expression.Identifier(Some name1, name2)

  let word word = pstringCI word >>. spaces

  let words words =
    words |> List.map word |> List.reduce (>>.)

  let between c1 c2 p =
    pchar c1 >>. spaces >>. p .>> pchar c2 .>> spaces

  let inParenthesis p = between '(' ')' p

  let commaSeparated p = sepBy1 p (pchar ',' .>> spaces)

  let star = word "*" |>> fun _ -> Expression.Star
  // This custom numbers parser is needed as both pint32 and pfloat are eager
  // to the point of not being possible to combine. pint32 would parse 1.2 as 1,
  // and pfloat would parse 1 as 1.0.
  let number =
    pint32 .>>. opt (pchar '.' >>. many (pchar '0') .>>. pint32) .>> spaces
    |>> fun (wholeValue, decimalPartOption) ->
          match decimalPartOption with
          | None -> Expression.Integer wholeValue
          | Some (leadingZeros, decimalValue) ->
              let divisor = List.length leadingZeros + 1
              let decimalPart = (float decimalValue) / (float <| pown 10 divisor)
              Expression.Float(float wholeValue + decimalPart)

  let boolean =
    (word "true" |>> fun _ -> Expression.Boolean true)
    <|> (word "false" |>> fun _ -> Expression.Boolean false)

  let stringLiteral =
    skipChar '\'' >>. manySatisfy (fun c -> c <> '\'') .>> skipChar '\'' .>> spaces
    |>> Expression.String

  let spaceSepUnaliasedExpressions = many1 expr

  let functionExpression =
    simpleIdentifier .>>. inParenthesis expr .>> spaces
    |>> fun (funName, expr) -> Function(funName.ToLower(), [ expr ])

  let selectedExpression = expr .>>. opt (word "AS" >>. simpleIdentifier) |>> As

  let commaSepExpressions = commaSeparated selectedExpression .>> spaces

  let whereClause = word "WHERE" >>. expr

  let havingClause = word "HAVING" >>. expr

  let groupBy = words [ "GROUP"; "BY" ] .>> spaces >>. commaSeparated expr

  let distinct = opt (word "distinct") |>> Option.isSome

  let table = simpleIdentifier .>>. opt (word "AS" >>. simpleIdentifier) |>> Expression.Table

  let joinType =
    choice [
      word "JOIN" >>. preturn InnerJoin
      words [ "INNER"; "JOIN" ] >>. preturn InnerJoin
      words [ "LEFT"; "JOIN" ] >>. preturn LeftJoin
      words [ "RIGHT"; "JOIN" ] >>. preturn RightJoin
      words [ "FULL"; "JOIN" ] >>. preturn FullJoin
    ]

  let selectQuery, selectQueryRef = createParserForwardedToRef<Expression, unit> ()

  let subQuery =
    inParenthesis selectQuery .>>. simpleIdentifier
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
    selectQueryRef
    := word "SELECT"
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
                                       >>= fun havingClause ->
                                             let query =
                                               {
                                                 SelectDistinct = distinct
                                                 Expressions = columns
                                                 From = from
                                                 Where = whereClause
                                                 GroupBy = groupBy |> Option.defaultValue []
                                                 Having = havingClause
                                               }

                                             preturn (Expression.SelectQuery query)

  // This is sort of silly... but the operator precedence parser is case sensitive. This means
  // if we add a parser for AND, then it will fail if you write a query as And... Therefore
  // this function brute forces all cases of a word...
  let allCasingPermutations (s: string) =
    let rec createPermutations acc next =
      match acc, next with
      | [], c :: cs -> createPermutations [ $"%c{System.Char.ToLower(c)}"; $"%c{System.Char.ToUpper(c)}" ] cs
      | acc, c :: cs ->
          let newLower = acc |> List.map (fun opVariant -> $"%s{opVariant}%c{System.Char.ToLower(c)}")
          let newUpper = acc |> List.map (fun opVariant -> $"%s{opVariant}%c{System.Char.ToUpper(c)}")
          createPermutations (newLower @ newUpper) cs
      | acc, [] -> acc

    s.ToCharArray()
    |> Array.toList
    |> createPermutations []
    // To avoid duplicates of such things as upper and lowercase "+"
    |> Set.ofList
    |> Set.toList

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
  addPostfixOperator "is null" spaces 8 false Expression.IsNull
  addPostfixOperator "is not null" spaces 8 false (fun expr -> Expression.Not(Expression.IsNull expr))
  addInfixOperator "^" spaces 9 Associativity.Left (fun left right -> Expression.Function("^", [ left; right ]))

  opp.TermParser <-
    choice [
      (attempt selectQuery)
      (attempt functionExpression)
      inParenthesis expr
      star
      number
      boolean
      stringLiteral
      identifier
    ]

  let fullParser = spaces >>. selectQuery .>> (opt (pchar ';')) .>> spaces .>> eof

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

type SqlParserError = string

let parseResult sql : Result<SelectQuery, SqlParserError> =
  match FParsec.CharParsers.run QueryParser.fullParser sql with
  | FParsec.CharParsers.Success (result, _, _) ->
      match result with
      | SelectQuery selectQuery -> Ok selectQuery
      | _ -> Error "Parse error: Expecting SELECT query"
  | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error("Parse error: " + errorMessage)

let parse sql = sql |> parseResult |> Result.value
