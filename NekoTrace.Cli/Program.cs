using NekoTrace.Cli;
using System.Text;

// The server sends UTF-8 and its output carries µs, × and → in it. Console.OpenStandardOutput writes those
// bytes through untouched, which is what a pipe wants; this is what stops a Windows console rendering them
// as mojibake on the way past. It throws when there is no console attached to set the code page of, which is
// exactly the case where it did not matter.
try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
}

return await NekoTraceCli.Create().Parse(args).InvokeAsync();
