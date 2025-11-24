using System.Collections.Generic;

namespace Kursovoi.Models
{
    public class CategoryViewModel
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class CatalogViewModel
    {
        public List<CategoryViewModel> Categories { get; set; } = new List<CategoryViewModel>();
        public List<ProductViewModel> TopDiscounted { get; set; } = new List<ProductViewModel>();
    }
}
