///////////////////////////////////////////////////////////////////////////////
//
// Copyright (c) 2016-2026 VASTreaming
//
// Licensee is granted permission to use, copy and modify this file.
// Licensee can distribute and sell this file in a binary form as a part of the
// licensee's product. Licensee is prohibited from selling this file and
// library separately from the licensee's products. Licensee is prohibited from
// disclosing this file to any 3rd party. Licensee is prohibited from openly
// publishing this file as a part of open-source software or any other means.
//
///////////////////////////////////////////////////////////////////////////////

using System;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VAST.Demo.Pages
{

    public class IndexModel : PageModel
    {

        [FromQuery(Name = "DisplayText")]
        public string DisplayText { get; set; } = "Hello World";

        public string Message { get; private set; } = "PageModel in C#";

        public void OnGet()
        {
            Program.DisplayText = this.DisplayText;
            Message += $"\nServer time is {DateTime.Now}";
        }

    }

}
