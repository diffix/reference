module OpenDiffix.Core.Parser

open OpenDiffix.Core
open OpenDiffix.Core.ParserTypes

module QueryParser =
  open FParsec

  let opp = OperatorPrecedenceParser<Expression, unit, unit>()

  let expr = opp.ExpressionParser

  let simpleIdentifier =
    let isIdentifierFirstChar token = isLetter token
    let isIdentifierChar token = isLetter token || isDigit token || token = '.' || token = '_'
    many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier" .>> spaces

  let identifier = simpleIdentifier |>> Expression.Identifier

  let word word = pstringCI word >>. spaces

  let words words = words |> List.map (word) |> List.reduce (>>.)

  let between c1 c2 p = pchar c1 >>. spaces >>. p .>> pchar c2 .>> spaces

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
    skipChar '\'' >>. manySatisfy (fun c -> c <> '\'') .>> skipChar '\''
    |>> Expression.String

  let spaceSepUnaliasedExpressions = many1 expr

  let functionExpression =
    simpleIdentifier .>>. inParenthesis expr .>> spaces
    |>> fun (funName, expr) -> Function(funName, [ expr ])

  let commaSepExpressions = commaSeparated expr .>> spaces

  let whereClause = word "WHERE" >>. expr

  let havingClause = word "HAVING" >>. expr

  let groupBy = words [ "GROUP"; "BY" ] .>> spaces >>. commaSeparated expr

  let distinct = opt (word "distinct") |>> Option.isSome

  let from = word "FROM" >>. identifier

  let selectQuery =
    word "SELECT"
    >>= fun _ ->
      distinct
      >>= fun distinct ->
            commaSepExpressions
            >>= fun columns ->
                  from
                  >>= fun table ->
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
                                              From = table
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
  addInfixOperator "as" spaces 1 Associativity.Left (fun left right -> Expression.As(left, right))
  addInfixOperator "and" spaces 1 Associativity.Left (fun left right -> Expression.And(left, right))

  addInfixOperator "or" (notFollowedBy (word "der by") .>> spaces) 2 Associativity.Left
  <| (fun left right -> Expression.Or(left, right))

  addInfixOperator "=" spaces 3 Associativity.Left (fun left right -> Expression.Equal(left, right))
  addInfixOperator "<>" spaces 3 Associativity.Left (fun left right -> Expression.Not(Expression.Equal(left, right)))
  addInfixOperator ">" spaces 3 Associativity.Left (fun left right -> Expression.Gt(left, right))
  addInfixOperator "<" spaces 3 Associativity.Left (fun left right -> Expression.Lt(left, right))
  addInfixOperator "<=" spaces 3 Associativity.Left (fun left right -> Expression.LtE(left, right))
  addInfixOperator ">=" spaces 3 Associativity.Left (fun left right -> Expression.GtE(left, right))
  addInfixOperator "+" spaces 4 Associativity.Left (fun left right -> Expression.Function("+", [ left; right ]))
  addInfixOperator "-" spaces 4 Associativity.Left (fun left right -> Expression.Function("-", [ left; right ]))
  addInfixOperator "*" spaces 5 Associativity.Left (fun left right -> Expression.Function("*", [ left; right ]))
  addInfixOperator "/" spaces 5 Associativity.Left (fun left right -> Expression.Function("/", [ left; right ]))
  addInfixOperator "%" spaces 6 Associativity.Left (fun left right -> Expression.Function("%", [ left; right ]))
  addPrefixOperator "not" spaces 7 false Expression.Not
  addPostfixOperator "is null" spaces 8 false (fun expr -> Expression.Equal(expr, Expression.Null))
  addPostfixOperator "is not null" spaces 8 false (fun expr -> Expression.Not(Expression.Equal(expr, Expression.Null)))
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

type SqlParserError = CouldNotParse of string

let parse sql: Result<SelectQuery, SqlParserError> =
  match FParsec.CharParsers.run QueryParser.fullParser sql with
  | FParsec.CharParsers.Success (result, _, _) ->
      match result with
      | SelectQuery selectQuery -> Ok selectQuery
      | _ -> Error(CouldNotParse "Expecting SELECT query")
  | FParsec.CharParsers.Failure (errorMessage, _, _) -> Error(CouldNotParse errorMessage)
