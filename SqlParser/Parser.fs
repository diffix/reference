namespace SqlParser

module ParserDefinition =
    open FParsec
    open SqlParser.Query
    
    let pAnyWord = many1Satisfy isLetter
    
    let pSkipWordSpacesCI word = skipStringCI word >>. spaces
    
    let pSkipWordsCI words =
        words
        |> List.map(pSkipWordSpacesCI)
        |> List.reduce (>>.)
        
    module ShowQueries =
        let pIdentifiersColumnsInTable = pSkipWordsCI ["columns"; "from"] >>. pAnyWord |>> ShowColumnsInTable 
            
        let pIdentifierTables = stringCIReturn "tables" ShowTables
        
        let parse = skipStringCI "show" >>. spaces >>. (pIdentifierTables <|> pIdentifiersColumnsInTable)
        
    let pQuery = spaces >>. ShowQueries.parse
        
    let parse = run pQuery 

module Parser =
    open SqlParser.Query

    type SqlParserError =
        | CouldNotParse of string
        
    let parseSql sqlString: Result<Query, SqlParserError> =
        match FParsec.CharParsers.run ParserDefinition.pQuery sqlString with
        | FParsec.CharParsers.Success (result, _, _) -> Ok result
        | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error (CouldNotParse errorMessage)