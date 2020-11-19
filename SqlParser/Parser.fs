namespace SqlParser

module ParserDefinition =
    open FParsec
    open SqlParser.Query
    
    let pAnyWord =
       satisfy isLetter .>>. manySatisfy (fun c -> isLetter c || isDigit c || c = '_')
       |>> fun (char, remainingColumnName) -> char.ToString() + remainingColumnName
    
    let pSkipWordSpacesCI word = skipStringCI word >>. spaces
    
    let pSkipWordsCI words =
        words
        |> List.map(pSkipWordSpacesCI)
        |> List.reduce (>>.)
        
    module ShowQueries =
        let pIdentifiersColumnsInTable = pSkipWordsCI ["COLUMNS"; "FROM"] >>. (pAnyWord <?> "table name") |>> ShowColumnsFromTable 
            
        let pIdentifierTables = stringCIReturn "TABLES" ShowTables
        
        let parse = skipStringCI "SHOW" >>. spaces >>. (pIdentifierTables <|> pIdentifiersColumnsInTable)
        
    module SelectQueries =
       let pColumn =
           pAnyWord .>> spaces
           |>> fun columnName -> Column (PlainColumn columnName)
       
       let pTable =
           pAnyWord .>> spaces |>> Table 
       
       let pColumns =
           sepBy1 pColumn (pchar ',' .>> spaces)
           .>> spaces
           
       let parse =
           pSkipWordSpacesCI "SELECT"
           >>. pColumns
           .>> pSkipWordSpacesCI "FROM"
           .>>. pTable
           |>> fun (columns, table) -> SelectQuery {Expressions = columns; From = table}
        
    let pQuery =
        spaces
        >>. (attempt ShowQueries.parse <|> SelectQueries.parse)
        
    let parse = run pQuery 

module Parser =
    open SqlParser.Query

    type SqlParserError =
        | CouldNotParse of string
        
    let parseSql sqlString: Result<Query, SqlParserError> =
        match FParsec.CharParsers.run ParserDefinition.pQuery sqlString with
        | FParsec.CharParsers.Success (result, _, _) -> Ok result
        | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error (CouldNotParse errorMessage)