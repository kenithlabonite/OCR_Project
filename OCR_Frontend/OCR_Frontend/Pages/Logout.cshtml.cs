using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            // Remove the authentication cookie
            Response.Cookies.Delete("AuthToken");
            // Redirect to the login page after logout
            return RedirectToPage("/Login");
        }
    }
}
