using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OCR_Frontend.Pages
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet()
        {
            // Remove all session cookies
            Response.Cookies.Delete("AuthToken");
            Response.Cookies.Delete("DisplayName");
            Response.Cookies.Delete("DisplayEmail");
            Response.Cookies.Delete("UserRole");
            // Redirect to the login page after logout
            return RedirectToPage("/Login");
        }
    }
}
