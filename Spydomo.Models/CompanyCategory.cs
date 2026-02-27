using System;
using System.Collections.Generic;
using System.Text;

namespace Spydomo.Models
{
    public partial class CompanyCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual ICollection<Company> Companies { get; set; } = new List<Company>();
    }
}
