public class PrintConfig
{
    public int port { get; set; }
    public string paper_size { get; set; }
}

public class DeviceConfig
{
    public bool is_middleware_configured { get; set; }
    public PrintConfig print_config { get; set; }
}

public class DepartmentConfig
{
    public int id { get; set; }
    public bool is_middleware_configured { get; set; }
    public PrintConfig print_config { get; set; }
}

public class PrintConfigRequest
{
    public DeviceConfig device { get; set; }
    public List<DepartmentConfig> departments { get; set; }
}