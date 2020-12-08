namespace SqlParser

module ParserDefinition =
    open FParsec
    open SqlParser.Query
    
    let pAnyWord =
       satisfy isLetter .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
       .>> spaces
       |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName
    
    let pSkipWordSpacesCI word = skipStringCI word >>. spaces
    
    let pSkipWordsCI words =
        words
        |> List.map(pSkipWordSpacesCI)
        |> List.reduce (>>.)
        
    let pInParenthesis p =
        pchar '(' >>. spaces >>. p .>> pchar ')' .>> spaces
        
    let pCommaSeparated p =
        sepBy1 p (pchar ',' .>> spaces)
        
    module ShowQueries =
        let pIdentifiersColumnsInTable = pSkipWordsCI ["COLUMNS"; "FROM"] >>. (pAnyWord <?> "table name") |>> ShowColumnsFromTable 
            
        let pIdentifierTables = stringCIReturn "TABLES" ShowTables
        
        let parse = skipStringCI "SHOW" >>. spaces >>. (pIdentifierTables <|> pIdentifiersColumnsInTable)
        
    module SelectQueries =
       let pPlainColumn =
           pAnyWord
           |>> PlainColumn
           
       let pColumn =
           pPlainColumn
           |>> Column 
       
       let pTable =
           pAnyWord |>> Table 
       
       let pFunction =
           pAnyWord .>>. pInParenthesis pColumn
           |>> Function
           
       let pDistinctColumn =
           pstringCI "distinct" .>> spaces >>. pPlainColumn
           |>> Distinct
               
       let pAggregate =
           pstringCI "count" .>> spaces >>. pInParenthesis pDistinctColumn
           |>> AnonymizedCount
           |>> AggregateFunction
       
       let pExpressions =
           pCommaSeparated (choice [pAggregate; attempt pFunction; pColumn]) 
           .>> spaces
           
       let pGroupBy =
           pSkipWordsCI ["GROUP"; "BY"]
           .>> spaces 
           >>. pCommaSeparated pAnyWord
           
       let parse =
           pSkipWordSpacesCI "SELECT"
           >>. pExpressions
           .>> pSkipWordSpacesCI "FROM"
           .>>. pTable
           .>>. opt pGroupBy
           |>> function
               | (columns, table), None ->
                   SelectQuery {Expressions = columns; From = table}
               | (columns, table), Some groupByColumns ->
                   AggregateQuery {Expressions = columns; From = table; GroupBy = groupByColumns}
        
    let pSkipSemiColon =
        optional (skipSatisfy ((=) ';')) .>> spaces
        
    let pQuery =
        spaces
        >>. (attempt ShowQueries.parse <|> SelectQueries.parse)
        .>> pSkipSemiColon
        .>> eof
        
    let parse = run pQuery 

module Parser =
    open SqlParser.Query

    type SqlParserError =
        | CouldNotParse of string
        
    let parseSql sqlString: Result<Query, SqlParserError> =
        match FParsec.CharParsers.run ParserDefinition.pQuery sqlString with
        | FParsec.CharParsers.Success (result, _, _) -> Ok result
        | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error (CouldNotParse errorMessage)