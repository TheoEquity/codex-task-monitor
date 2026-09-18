Option Explicit

If WScript.Arguments.Count <> 2 Then
    WScript.Quit 2
End If

Function Quote(ByVal value)
    Quote = Chr(34) & value & Chr(34)
End Function

Dim shell, powershellPath, command
Set shell = CreateObject("WScript.Shell")
powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = Quote(powershellPath) & _
    " -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " & _
    Quote(WScript.Arguments(0)) & " -Mode Launch -ExecutablePath " & Quote(WScript.Arguments(1))

shell.Run command, 0, False
