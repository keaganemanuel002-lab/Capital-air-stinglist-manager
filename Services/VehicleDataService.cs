using System;
using System.Collections.Generic;
using System.Linq;

namespace StingListManager.Services;

public class VehicleMake
{
    public string Name { get; set; } = "";
    public List<string> Models { get; set; } = new();
}

public class VehicleDataService
{
    private static readonly List<VehicleMake> PassengerVehicles = new()
    {
        new VehicleMake { Name = "Toyota", Models = new() { "Fortuner", "Hiace", "Hilux", "Land Cruiser", "Quantum", "Corolla", "Yaris", "Camry", "Prius", "RAV4" } },
        new VehicleMake { Name = "Ford", Models = new() { "Ranger", "Transit", "Fiesta", "Focus", "Mustang", "Everest", "EcoSport" } },
        new VehicleMake { Name = "Volkswagen", Models = new() { "Polo", "Golf", "Passat", "Amarok", "Transporter", "Caddy", "Touareg" } },
        new VehicleMake { Name = "Nissan", Models = new() { "Navara", "NV200", "NV350", "Qashqai", "X-Trail", "Almera", "Micra" } },
        new VehicleMake { Name = "Hyundai", Models = new() { "H100", "H350", "i10", "i20", "Tucson", "Santa Fe", "Creta" } },
        new VehicleMake { Name = "Isuzu", Models = new() { "D-Max", "KB", "NPR", "N-Series", "F-Series" } },
        new VehicleMake { Name = "Mercedes-Benz", Models = new() { "Sprinter", "Vito", "C-Class", "E-Class", "S-Class", "GLC", "GLE" } },
        new VehicleMake { Name = "BMW", Models = new() { "3 Series", "5 Series", "7 Series", "X3", "X5", "X7", "i3", "i4" } },
        new VehicleMake { Name = "Audi", Models = new() { "A3", "A4", "A6", "A8", "Q3", "Q5", "Q7", "Q8" } },
        new VehicleMake { Name = "Chevrolet", Models = new() { "Cruze", "Spark", "Aveo", "Captiva" } },
        new VehicleMake { Name = "Mahindra", Models = new() { "Bolero", "Scorpio", "KUV100", "XUV300", "XUV500" } },
        new VehicleMake { Name = "Kia", Models = new() { "Sportage", "Seltos", "Sorento", "Rio", "Cerato", "Picanto" } },
        new VehicleMake { Name = "Renault", Models = new() { "Kangoo", "Master", "Duster", "Sandero", "Clio" } },
        new VehicleMake { Name = "Opel", Models = new() { "Vivaro", "Combo", "Corsa", "Astra", "Insignia" } },
        new VehicleMake { Name = "Suzuki", Models = new() { "Swift", "Vitara", "S-Presso", "Ertiga" } },
        new VehicleMake { Name = "Mitsubishi", Models = new() { "L200", "Outlander", "Mirage", "Attrage" } },
        new VehicleMake { Name = "Peugeot", Models = new() { "Partner", "Expert", "3008", "5008" } },
        new VehicleMake { Name = "Citroen", Models = new() { "Berlingo", "C3", "C4", "C5" } },
        new VehicleMake { Name = "Honda", Models = new() { "Civic", "Accord", "CR-V", "HR-V", "Jazz" } },
        new VehicleMake { Name = "Subaru", Models = new() { "Outback", "Impreza", "Forester", "XV", "Levorg" } },
        new VehicleMake { Name = "Mazda", Models = new() { "CX-3", "CX-5", "CX-9", "3", "6", "MX-5" } },
        new VehicleMake { Name = "Haval", Models = new() { "H2", "H6", "F7", "F7X", "H9" } },
        new VehicleMake { Name = "BYD", Models = new() { "Song", "QQ", "Yuan", "Seagull" } },
        new VehicleMake { Name = "Chery", Models = new() { "Tiggo", "QQ", "Arrizo" } },
        new VehicleMake { Name = "Lexus", Models = new() { "RX", "NX", "ES", "IS", "GX" } },
        new VehicleMake { Name = "Jeep", Models = new() { "Wrangler", "Cherokee", "Grand Cherokee", "Renegade" } },
        new VehicleMake { Name = "Dodge", Models = new() { "Ram", "Durango", "Journey" } },
        new VehicleMake { Name = "GMC", Models = new() { "Sierra", "Yukon", "Terrain" } },
    };

    private static readonly List<VehicleMake> CommercialVehicles = new()
    {
        new VehicleMake { Name = "Volvo", Models = new() { "FH16", "FH12", "FH", "FM", "FMX", "VNX", "VNL" } },
        new VehicleMake { Name = "Scania", Models = new() { "R440", "R450", "R500", "P440", "P450", "P500", "G440", "G450" } },
        new VehicleMake { Name = "Man", Models = new() { "TGX 26.540", "TGX 28.540", "TGA", "TGM", "TGL" } },
        new VehicleMake { Name = "Mercedes-Benz", Models = new() { "Actros", "Axor", "Econic", "Antos" } },
        new VehicleMake { Name = "Hino", Models = new() { "300 Series", "500 Series", "700 Series", "GH" } },
        new VehicleMake { Name = "FAW", Models = new() { "J6", "CA1041", "CA5093", "CA6DM" } },
        new VehicleMake { Name = "Sinotruk", Models = new() { "HOWO A7", "HOWO T7H", "HOWO WD615" } },
        new VehicleMake { Name = "Shacman", Models = new() { "F3000", "F2000", "L3000" } },
        new VehicleMake { Name = "Isuzu", Models = new() { "FRR", "FSR", "FVR", "FXZ" } },
        new VehicleMake { Name = "Nissan", Models = new() { "UD Tipper", "UD Crane", "Condor" } },
        new VehicleMake { Name = "Ford", Models = new() { "Cargo 1115", "Cargo 1119", "Cargo 2530" } },
        new VehicleMake { Name = "Tata", Models = new() { "3518", "4018", "5518", "6x4" } },
        new VehicleMake { Name = "Hyundai", Models = new() { "Mighty", "Xcient", "Sonac" } },
        new VehicleMake { Name = "Iveco", Models = new() { "Stralis", "Trakker", "Vertis" } },
        new VehicleMake { Name = "Renault", Models = new() { "Magnum", "Premium", "Midlum" } },
        new VehicleMake { Name = "Freightliner", Models = new() { "Cascadia", "Classic", "Business Class" } },
    };

    private static readonly List<VehicleMake> Generators = new()
    {
        new VehicleMake { Name = "Eskom", Models = new() { "Mobile Gen 100kVA", "Mobile Gen 250kVA", "Mobile Gen 500kVA" } },
        new VehicleMake { Name = "Caterpillar", Models = new() { "C7.1", "C9", "C15", "C18", "C27", "C32" } },
        new VehicleMake { Name = "Cummins", Models = new() { "C50D5", "C75D5", "C100D5", "C150D5" } },
        new VehicleMake { Name = "Perkins", Models = new() { "403A-11G1", "404D-22G", "1104A-4.4" } },
        new VehicleMake { Name = "Volkswagen", Models = new() { "VW Generator 50kW", "VW Generator 100kW" } },
        new VehicleMake { Name = "Himoinsa", Models = new() { "HYW-50", "HYW-100", "HYW-500" } },
        new VehicleMake { Name = "Diesel Generator", Models = new() { "10kVA", "25kVA", "50kVA", "100kVA", "250kVA", "500kVA" } },
        new VehicleMake { Name = "Stamford", Models = new() { "FG Wilson", "Powered by Perkins" } },
        new VehicleMake { Name = "Kohler", Models = new() { "30kW", "50kW", "100kW", "150kW" } },
    };

    private static readonly List<VehicleMake> Trailers = new()
    {
        new VehicleMake { Name = "Cargo Trailer", Models = new() { "Flatbed", "Box Trailer", "Drop Deck", "Refrigerated", "Tanker" } },
        new VehicleMake { Name = "Utility Trailer", Models = new() { "Single Axle", "Dual Axle", "Tandem" } },
        new VehicleMake { Name = "Lowbed Trailer", Models = new() { "15 Ton", "25 Ton", "35 Ton", "50 Ton" } },
        new VehicleMake { Name = "Dump Trailer", Models = new() { "Single Axle", "Tandem", "Tri-axle", "Rock Trailer" } },
        new VehicleMake { Name = "Livestock Trailer", Models = new() { "2 Axle", "3 Axle", "Cattle Truck" } },
        new VehicleMake { Name = "Tanker Trailer", Models = new() { "Water Tanker", "Fuel Tanker", "Chemical Tanker", "Milk Tanker" } },
        new VehicleMake { Name = "Refrigerated Trailer", Models = new() { "Insulated", "Reefer Unit", "Chiller" } },
        new VehicleMake { Name = "Car Carrier", Models = new() { "2 Car", "3 Car", "4 Car", "6 Car" } },
        new VehicleMake { Name = "Enclosed Trailer", Models = new() { "Single Axle", "Tandem", "Tri-axle" } },
        new VehicleMake { Name = "Machinery Trailer", Models = new() { "Gooseneck", "Pintle Hitch", "Detachable" } },
    };

    public List<string> GetAllVehicleMakes()
    {
        return PassengerVehicles.Select(v => v.Name).OrderBy(x => x).ToList();
    }

    public List<string> GetAllTruckMakes()
    {
        return CommercialVehicles.Select(v => v.Name).OrderBy(x => x).ToList();
    }

    public List<string> GetAllGeneratorMakes()
    {
        return Generators.Select(v => v.Name).OrderBy(x => x).ToList();
    }

    public List<string> GetAllTrailerTypes()
    {
        return Trailers.Select(v => v.Name).OrderBy(x => x).ToList();
    }

    public List<string> GetVehicleModelsByMake(string make)
    {
        var vehicle = PassengerVehicles.FirstOrDefault(v => v.Name.Equals(make, StringComparison.OrdinalIgnoreCase));
        return vehicle?.Models.OrderBy(x => x).ToList() ?? new();
    }

    public List<string> GetTruckModelsByMake(string make)
    {
        var vehicle = CommercialVehicles.FirstOrDefault(v => v.Name.Equals(make, StringComparison.OrdinalIgnoreCase));
        return vehicle?.Models.OrderBy(x => x).ToList() ?? new();
    }

    public List<string> GetGeneratorModelsByMake(string make)
    {
        var generator = Generators.FirstOrDefault(v => v.Name.Equals(make, StringComparison.OrdinalIgnoreCase));
        return generator?.Models.OrderBy(x => x).ToList() ?? new();
    }

    public List<string> GetTrailerModelsByType(string type)
    {
        var trailer = Trailers.FirstOrDefault(v => v.Name.Equals(type, StringComparison.OrdinalIgnoreCase));
        return trailer?.Models.OrderBy(x => x).ToList() ?? new();
    }
}
