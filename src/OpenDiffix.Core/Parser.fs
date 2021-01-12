module OpenDiffix.Core.Parser

open OpenDiffix.Core.ParserTypes

module Definitions =
  open FParsec

  let anyWord =
    satisfy isLetter .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
    .>> spaces
    |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName

  let stringConstant = manySatisfy (fun c -> c <> '"')

  let skipWordSpacesCI word = skipStringCI word >>. spaces

  let skipWordsCI words = words |> List.map (skipWordSpacesCI) |> List.reduce (>>.)

  let inParenthesis p = pchar '(' >>. spaces >>. p .>> pchar ')' .>> spaces

  let inQuotes p = pchar '"' >>. spaces >>. p .>> pchar '"' .>> spaces

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
    let parseNot = skipWordSpacesCI "not" |>> fun _ -> Not
    let parsePlus = charTo '+' Plus
    let parseMinus = charTo '-' Minus
    let parseStar = charTo '*' Star
    let parseSlash = charTo '/' Slash
    let parseHat = charTo '^' Hat

    let operator = choice [parseNot; parsePlus; parseMinus; parseStar; parseSlash; parseHat] |>> Expression.Operator

    let constants =
      choice [
        attempt (pint32 .>> spaces |>> Constant.Integer)
        attempt (inQuotes stringConstant |>> Constant.String)
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

    let groupBy = skipWordsCI [ "GROUP"; "BY" ] .>> spaces >>. commaSeparated expression

    let parse =
      skipWordSpacesCI "SELECT" >>. commaSepExpressions .>> skipWordSpacesCI "FROM"
      .>>. table
      .>>. opt groupBy
      |>> function
      | (columns, table), groupByColumnsOption ->
        SelectQuery { Expressions = columns; From = table; GroupBy = groupByColumnsOption |> Option.defaultValue [] }

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
