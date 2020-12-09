module OpenDiffix.Core.Parser

open OpenDiffix.Core.ParserTypes

module Definitions =
  open FParsec

  let anyWord =
    satisfy isLetter
    .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
    .>> spaces
    |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName

  let skipWordSpacesCI word = skipStringCI word >>. spaces

  let skipWordsCI words =
    words |> List.map (skipWordSpacesCI) |> List.reduce (>>.)

  let inParenthesis p =
    pchar '(' >>. spaces >>. p .>> pchar ')' .>> spaces

  let commaSeparated p = sepBy1 p (pchar ',' .>> spaces)

  module ShowQueries =
    let identifiersColumnsInTable =
      skipWordsCI [ "COLUMNS"; "FROM" ]
      >>. (anyWord |>> TableName <?> "table name")
      |>> ShowColumnsFromTable

    let identifierTables = stringCIReturn "TABLES" ShowTables

    let parse =
      skipStringCI "SHOW"
      >>. spaces
      >>. (identifierTables <|> identifiersColumnsInTable)

  module SelectQueries =
    let columnName = anyWord .>> spaces |>> ColumnName

    let plainColumn = columnName |>> PlainColumn

    let column = plainColumn |>> Column

    let table =
      anyWord |>> fun tableName -> Table(TableName tableName)

    let ``function`` = anyWord .>>. inParenthesis column |>> Function

    let distinctColumn =
      pstringCI "distinct" .>> spaces >>. columnName |>> Distinct

    let aggregate =
      pstringCI "count" .>> spaces
      >>. inParenthesis distinctColumn
      |>> AnonymizedCount
      |>> AggregateFunction

    let expressions =
      commaSeparated (choice [ aggregate; attempt ``function``; column ])
      .>> spaces

    let groupBy =
      skipWordsCI [ "GROUP"; "BY" ] .>> spaces
      >>. commaSeparated columnName

    let parse =
      skipWordSpacesCI "SELECT" >>. expressions
      .>> skipWordSpacesCI "FROM"
      .>>. table
      .>>. opt groupBy
      |>> function
      | (columns, table), None -> SelectQuery { Expressions = columns; From = table }
      | (columns, table), Some groupByColumns ->
          AggregateQuery
            { Expressions = columns
              From = table
              GroupBy = groupByColumns }

  let skipSemiColon = optional (skipSatisfy ((=) ';')) .>> spaces

  let query =
    spaces
    >>. (attempt ShowQueries.parse <|> SelectQueries.parse)
    .>> skipSemiColon
    .>> eof

  let parse = run query

type SqlParserError = CouldNotParse of string

let parse sql: Result<Query, SqlParserError> =
  match FParsec.CharParsers.run Definitions.query sql with
  | FParsec.CharParsers.Success (result, _, _) -> Ok result
  | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error(CouldNotParse errorMessage)
