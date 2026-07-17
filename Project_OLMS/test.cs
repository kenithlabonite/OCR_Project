using System;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var request = new GenerateContentRequest("Analyze this");
        var part = new Part();
        part.InlineData = new GenerativeAI.Types.Blob { MimeType = "application/pdf", Data = "base64..." };
        // wait, GenerativeAI.Types is a namespace? No, Mscc.GenerativeAI.Types.
    }
}
