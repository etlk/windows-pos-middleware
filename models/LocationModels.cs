using System.Collections.Generic;

namespace MiddlewareApp.Models
{
    public class RootData
    {
        public Data data { get; set; }
    }

    public class Data
    {
        public List<Location> locations { get; set; }
        // You can add business_settings, order_sources etc. later
    }

    public class Location
    {
        
        public int id { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public string city { get; set; }
        public string country { get; set; }
        public string long_address { get; set; }

        public List<Device> devices { get; set; }
        public List<Department> departments { get; set; }
        // add other fields if needed
    }
}