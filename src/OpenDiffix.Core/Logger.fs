module OpenDiffix.Core.Logger

open System

type LogLevel =
  | DebugLevel
  | InfoLevel
  | WarningLevel

type LogMessage = { Timestamp: DateTime; Level: LogLevel; Message: string }

type LoggerBackend = LogMessage -> unit

module LogMessage =
  let private levelToString =
    function
    | DebugLevel -> "[DBG]"
    | InfoLevel -> "[INF]"
    | WarningLevel -> "[WRN]"

  let toString (message: LogMessage) : string =
    $"""{message.Timestamp.ToString("HH:mm:ss.fff")} {levelToString message.Level} {message.Message}"""

// ----------------------------------------------------------------
// Public API
// ----------------------------------------------------------------

let mutable backend: LoggerBackend = fun _msg -> ()

let inline private log level message =
  backend { Timestamp = DateTime.Now; Level = level; Message = message }

let debug (message: string) : unit = log DebugLevel message

let info (message: string) : unit = log InfoLevel message

let warning (message: string) : unit = log WarningLevel message
