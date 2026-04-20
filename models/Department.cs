using System;

namespace MiddlewareApp.Models
{
    public class Department
    {

        public int id { get; set; }
        public string name { get; set; }
        public string code { get; set; }
        public int is_active { get; set; }
        public string? selected_printer { get; internal set; }

        public bool configured { get; set; } = false;
    }
}