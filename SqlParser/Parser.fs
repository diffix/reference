namespace SqlParser

module ParserDefinition =
    open FParsec
    open SqlParser.Query
    
    let anyWord =
       satisfy isLetter .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
       .>> spaces
       |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName
    
    let skipWordSpacesCI word = skipStringCI word >>. spaces
    
    let skipWordsCI words =
        words
        |> List.map(skipWordSpacesCI)
        |> List.reduce (>>.)
        
    let inParenthesis p =
        pchar '(' >>. spaces >>. p .>> pchar ')' .>> spaces
        
    let commaSeparated p =
        sepBy1 p (pchar ',' .>> spaces)
        
    module ShowQueries =
        let identifiersColumnsInTable = skipWordsCI ["COLUMNS"; "FROM"] >>. (anyWord <?> "table name") |>> ShowColumnsFromTable 
            
        let identifierTables = stringCIReturn "TABLES" ShowTables
        
        let parse = skipStringCI "SHOW" >>. spaces >>. (identifierTables <|> identifiersColumnsInTable)
        
    module SelectQueries =
       let plainColumn =
           anyWord
           |>> PlainColumn
           
       let column =
           plainColumn
           |>> Column 
       
       let table =
           anyWord |>> Table 
       
       let ``function`` =
           anyWord .>>. inParenthesis column
           |>> Function
           
       let distinctColumn =
           pstringCI "distinct" .>> spaces >>. plainColumn
           |>> Distinct
               
       let aggregate =
           pstringCI "count" .>> spaces >>. inParenthesis distinctColumn
           |>> AnonymizedCount
           |>> AggregateFunction
       
       let expressions =
           commaSeparated (choice [aggregate; attempt ``function``; column]) 
           .>> spaces
           
       let groupBy =
           skipWordsCI ["GROUP"; "BY"]
           .>> spaces 
           >>. commaSeparated anyWord
           
       let parse =
           skipWordSpacesCI "SELECT"
           >>. expressions
           .>> skipWordSpacesCI "FROM"
           .>>. table
           .>>. opt groupBy
           |>> function
               | (columns, table), None ->
                   SelectQuery {Expressions = columns; From = table}
               | (columns, table), Some groupByColumns ->
                   AggregateQuery {Expressions = columns; From = table; GroupBy = groupByColumns}
        
    let skipSemiColon =
        optional (skipSatisfy ((=) ';')) .>> spaces
        
    let query =
        spaces
        >>. (attempt ShowQueries.parse <|> SelectQueries.parse)
        .>> skipSemiColon
        .>> eof
        
    let parse = run query 

module Parser =
    open SqlParser.Query

    type SqlParserError =
        | CouldNotParse of string
        
    let parseSql sqlString: Result<Query, SqlParserError> =
        match FParsec.CharParsers.run ParserDefinition.query sqlString with
        | FParsec.CharParsers.Success (result, _, _) -> Ok result
        | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error (CouldNotParse errorMessage)