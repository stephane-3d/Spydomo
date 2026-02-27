using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Spydomo.Models
{
    public class SignalType
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Slug { get; set; } = "";   // "feature-launch"

        [MaxLength(64)]
        public string Name { get; set; } = "";   // "Feature Launch"

        [MaxLength(512)]
        public string Description { get; set; } = "";

        public bool AllowedInLlm { get; set; }   // only Content ones should be true
    }
}
