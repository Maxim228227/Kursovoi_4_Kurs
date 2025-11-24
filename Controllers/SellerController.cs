using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Kursovoi.Models;
using System.Linq;
using System.Collections.Generic;

namespace Kursovoi.Controllers
{
    [Authorize(Roles = "seller")]
    public class SellerController : Controller
    {
        // GET: /Seller/Index
        public IActionResult Index()
        {
            // build a simple seller dashboard view model
            var vm = new SellerPanelViewModel
            {
                Products = new List<ProductViewModel>(),
                Store = new StoreViewModel(),
                Stocks = new List<StockViewModel>()
            };

            // TODO: fill with real data by calling UdpClientHelper endpoints (if available)
            return View(vm);
        }

        // other seller actions (CRUD) can be added later
    }
}
