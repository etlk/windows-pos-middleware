using System;

namespace MiddlewareApp.Models
{
    public class Device
    {
        

        public int id { get; set; }
        public string device_name { get; set; }
        public string serial_number { get; set; }
        public string device_status { get; set; } // e.g., "active"
        public string type { get; set; }          // e.g., "point_of_sale"
        public string printer_type { get; set; }
        public int location_id { get; set; }

        public bool configured { get; set; } = false;


        public string selected_printer { get; set; }
        // Add more fields as needed from JSON
        // public object pos_register { get; set; }
        // public object settings { get; set; }
    }
}