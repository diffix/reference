module OpenDiffix.Core.Parser

open OpenDiffix.Core.ParserTypes

module Definitions =
  open FParsec

  let anyWord =
    satisfy isLetter .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
    .>> spaces
    |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName

  let stringConstant = manySatisfy (fun c -> c <> '\'')

  let skipWordSpacesCI word = skipStringCI word >>. spaces

  let skipWordsCI words = words |> List.map (skipWordSpacesCI) |> List.reduce (>>.)

  let between c1 c2 p = pchar c1 >>. spaces >>. p .>> pchar c2 .>> spaces

  let inParenthesis p = between '(' ')' p

  let inSingleQuotes p = between '\'' '\'' p

  let commaSeparated p = sepBy1 p (pchar ',' .>> spaces)

  module ShowQueries =
    let identifiersColumnsInTable =
      skipWordsCI [ "COLUMNS"; "FROM" ] >>. (anyWord <?> "table name")
      |>> fun tableName -> Show (ShowQuery.Columns tableName)

    let identifierTables = stringCIReturn "TABLES" (Show ShowQuery.Tables)

    let parse =
      skipStringCI "SHOW"
      >>. spaces
      >>. (identifierTables <|> identifiersColumnsInTable)

  module SelectQueries =
    let charTo c v = satisfy (fun pc -> pc = c) |>> fun _ -> v
    let wordTo w v = skipWordSpacesCI w |>> fun _ -> v

    let parsePlus = charTo '+' Plus
    let parseMinus = charTo '-' Minus
    let parseStar = charTo '*' Star
    let parseSlash = charTo '/' Slash
    let parseHat = charTo '^' Hat
    let parseEqual = charTo '=' Operator.Equal
    let parseNotEqual = wordTo "<>" Operator.NotEqual
    let parseNot = wordTo "not" Operator.Not
    let parseLT = charTo '<' Operator.LT
    let parseGT = charTo '>' Operator.GT
    let parseAnd = wordTo "and" Operator.And
    let parseOr = wordTo "or" Operator.Or

    let operator =
      choice [
        parseNot; parsePlus; parseMinus; parseStar; parseSlash; parseHat
        parseEqual; parseNotEqual; parseLT; parseGT; parseAnd; parseOr
      ]
      |>> Expression.Operator

    let constants =
      choice [
        attempt (pint32 .>> spaces |>> Constant.Integer)
        attempt (inSingleQuotes stringConstant |>> Constant.String)
        choice [
          pstringCI "true" |>> fun _ -> Boolean true
          pstringCI "false" |>> fun _ -> Boolean false
        ]
      ]
      |>> Constant

    let term = choice [attempt constants; attempt operator; anyWord |>> Term] .>> spaces

    let table = anyWord |>> Table

    // Parsers are values, and as such cannot be made recursive.
    // Instead we have to use a forward reference that allows ut so
    // use the parser before it is defined.
    let expression, expressionRef = createParserForwardedToRef()
    let unaliasedExpression, unaliasedExpressionRef = createParserForwardedToRef()

    // Note that since the expression parser itself consumes trailing spaces
    // we do not use a sepBy1 or equivalent parser combinator here.
    let spaceSepUnaliasedExpressions = many1 unaliasedExpression

    let ``function`` = anyWord .>>. inParenthesis spaceSepUnaliasedExpressions |>> Function

    do unaliasedExpressionRef := choice [ attempt ``function``; term ]

    let alias =
      skipWordSpacesCI "as" >>. anyWord

    do expressionRef := unaliasedExpression .>>. opt alias
                        |>> (function
                            | expression, None -> expression
                            | expression, Some alias -> AliasedTerm (expression, alias))

    let commaSepExpressions = commaSeparated expression .>> spaces

    let parseBetween =
      unaliasedExpression .>> skipWordSpacesCI "between"
      .>>. unaliasedExpression .>> skipWordSpacesCI "and" .>>.unaliasedExpression
      |>> fun ((a, b), c) -> Condition.Between (a, b, c)

    let p1BetweenP2ToT p1 p2 t = p2 .>> spaces .>> p1 .>> spaces .>>. p2 .>> spaces |>> t

    let whereClauseCondition, whereClauseConditionRef = createParserForwardedToRef()

    do whereClauseConditionRef := choice [
        attempt (p1BetweenP2ToT parseEqual unaliasedExpression Condition.Equal)
        attempt (p1BetweenP2ToT parseNotEqual unaliasedExpression Condition.NotEqual)
        attempt (p1BetweenP2ToT parseGT unaliasedExpression Condition.GT)
        attempt (p1BetweenP2ToT parseLT unaliasedExpression Condition.LT)
        attempt (p1BetweenP2ToT parseLT unaliasedExpression Condition.LT)
        attempt (p1BetweenP2ToT parseLT unaliasedExpression Condition.LT)
        attempt (skipWordSpacesCI "not" >>. unaliasedExpression |>> Condition.Not)
        attempt parseBetween
        unaliasedExpression |>> Condition.IsTrue
      ]

    let whereClauseDisjunction =
      sepBy1 whereClauseCondition (skipWordSpacesCI "or")
      |>> List.reduce (fun a b -> Condition.Or (a, b))

    let whereClauseConjunction =
      sepBy1 whereClauseDisjunction (skipWordSpacesCI "and")
      |>> List.reduce (fun a b -> Condition.And (a, b))

    let whereClause = skipWordSpacesCI "WHERE" .>> spaces >>. whereClauseConjunction

    let groupBy = skipWordsCI [ "GROUP"; "BY" ] .>> spaces >>. commaSeparated expression

    let parse =
      skipWordSpacesCI "SELECT" >>. commaSepExpressions .>> skipWordSpacesCI "FROM"
      .>>. table
      .>>. opt whereClause
      .>>. opt groupBy
      |>> function
      | ((columns, table), whereClauseConditionOption), groupByColumnsOption ->
        SelectQuery {
          Expressions = columns
          From = table
          Where = whereClauseConditionOption
          GroupBy = groupByColumnsOption |> Option.defaultValue []
        }

  let skipSemiColon = optional (skipSatisfy ((=) ';')) .>> spaces

  let query =
    spaces >>. (attempt ShowQueries.parse <|> SelectQueries.parse)
    .>> skipSemiColon
    .>> eof

  let parse = run query

type SqlParserError = CouldNotParse of string

let parse sql: Result<Query, SqlParserError> =
  match FParsec.CharParsers.run Definitions.query sql with
  | FParsec.CharParsers.Success (result, _, _) -> Ok result
  | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error(CouldNotParse errorMessage)
