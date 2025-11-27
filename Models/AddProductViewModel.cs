using System.ComponentModel.DataAnnotations;

namespace Kursovoi.Models
{
    public class AddProductViewModel
    {
        [Required]
        [Display(Name = "Название товара")]
        public string ProductName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Категория")]
        public int CategoryId { get; set; }

        [Required]
        [Display(Name = "Производитель")]
        public int ManufacturerId { get; set; }

        [Display(Name = "Краткое описание")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Цена")]
        public decimal Price { get; set; }

        [Display(Name = "Скидка (например 0.15 для 15%)")]
        public decimal Discount { get; set; }

        [Required]
        [Display(Name = "Количество на складе")]
        public int Quantity { get; set; }

        [Display(Name = "Ссылка на фото")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
