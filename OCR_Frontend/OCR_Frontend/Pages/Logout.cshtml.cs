using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OCR_Frontend.Helpers;

namespace OCR_Frontend.Pages
{
    [IgnoreAntiforgeryToken]
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            AuthCookieHelper.ClearSessionCookies(Response);
            return RedirectToPage("/Login");
        }

        public IActionResult OnPost()
        {
            AuthCookieHelper.ClearSessionCookies(Response);
            return RedirectToPage("/Login");
        }
    }
}
