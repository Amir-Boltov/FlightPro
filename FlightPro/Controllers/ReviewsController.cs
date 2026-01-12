using Microsoft.AspNetCore.Mvc;

namespace FlightPro.Controllers
{
    public class ReviewsController : Controller
    {
        public IActionResult AddReview ()
        {
            return View();
        }
    }
}
