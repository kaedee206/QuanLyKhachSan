using QuanLyKhachSan.Models.Enums;

namespace QuanLyKhachSan.Services
{
    public class HotelMenuItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "Viet", "Asia", "Europe", "Beverage", "Dessert"
        public string CategoryName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public ServiceType ServiceType { get; set; }
        public string Department { get; set; } = "Bếp"; // "Bếp" or "Bar"
        public bool IsPopular { get; set; }

        public string GetDisplayImageUrl()
        {
            if (!string.IsNullOrEmpty(ImageUrl)) return ImageUrl;
            return Category switch
            {
                "Viet" => "/images/hotel-assets/menu/menu-pho-bo.jpg",
                "Europe" => "/images/hotel-assets/menu/menu-wagyu-steak.jpg",
                "Asia" => "/images/hotel-assets/menu/menu-asia.png",
                "Beverage" => "/images/hotel-assets/menu/menu-beverage.png",
                "Dessert" => "/images/hotel-assets/menu/menu-dessert.png",
                _ => "/images/hotel-assets/menu/menu-pho-bo.jpg"
            };
        }
    }

    public static class HotelMenuCatalog
    {
        public static readonly List<HotelMenuItem> Items = new()
        {
            // ═══ 1. MÓN VIỆT TRUYỀN THỐNG / BÌNH DÂN CAO CẤP ═══
            new HotelMenuItem
            {
                Id = "VN01",
                Name = "Phở Bò Tái Lăn Hà Nội",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 85000,
                Description = "Nước dùng ninh xương bò 24h thanh ngọt tự nhiên, thịt thăn bò phi lê xào lăn thơm lừng tỏi gừng tươi và hành hoa.",
                Icon = "🍜",
                ImageUrl = "/images/hotel-assets/menu/menu-pho-bo.jpg",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "VN02",
                Name = "Bún Bò Huế Cung Đình",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 95000,
                Description = "Hương vị cố đô đậm đà với bắp hoa bò Úc, chả cua Huế tươi, gân bò mềm ngậy, sả ớt rim cay nồng.",
                Icon = "🍲",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "VN03",
                Name = "Cơm Tấm Sườn Bì Chả Đặc Biệt",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 85000,
                Description = "Sườn cốt lết ướp mật ong nướng than hoa thơm nức mũi, bì thái sợi thủ công và chả trứng hấp béo ngậy, ăn kèm nước mắm chua ngọt.",
                Icon = "🍛",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "VN04",
                Name = "Bánh Mì Chảo Bò Wagyu Bate Cột Đèn",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 120000,
                Description = "Bò Wagyu thái mỏng mềm tan cùng pate Hải Phòng truyền thống béo ngậy, xúc xích tiêu, trứng ốp la lòng đào và bánh mì nóng giòn.",
                Icon = "🍳",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "VN05",
                Name = "Gỏi Cuốn Tôm Thịt Ngũ Sắc (4 cuốn)",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 65000,
                Description = "Tôm sú tươi roi rói kết hợp thịt ba chỉ cuộn rau thơm thanh mát trong bánh tráng dẻo, dùng kèm sốt tương đậu phộng béo bùi.",
                Icon = "🥗",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "VN06",
                Name = "Chả Giò Cua Bể Hải Phòng Giòn Rụm",
                Category = "Viet",
                CategoryName = "Món Việt Truyền Thống",
                Price = 90000,
                Description = "Thịt cua bể tươi béo ngọt cùng nấm hương, mộc nhĩ cuộn vỏ ram giòn tan rụm, chấm mắm đu đủ tỏi ớt.",
                Icon = "🥢",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },

            // ═══ 2. ẨM THỰC Á CHÂU TINH HOA ═══
            new HotelMenuItem
            {
                Id = "ASIA01",
                Name = "Cơm Chiên Dương Châu Hải Sản",
                Category = "Asia",
                CategoryName = "Ẩm Thực Á Châu",
                Price = 125000,
                Description = "Hạt cơm tơi vàng óng ả xào cùng tôm sú biển, mực tươi Cát Bà, lạp xưởng Mai Quế Lộ và hạt đậu Hà Lan giòn ngọt.",
                Icon = "🍚",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "ASIA02",
                Name = "Vịt Quay Bắc Kinh Da Giòn (Nửa con)",
                Category = "Asia",
                CategoryName = "Ẩm Thực Á Châu",
                Price = 280000,
                Description = "Da vịt óng ả căng bóng giòn tan, thịt vịt mềm thơm gia vị thảo mộc, ăn kèm bánh tráng mỏng, dưa leo, đầu hành và sốt ngọt Hoisin.",
                Icon = "🍗",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "ASIA03",
                Name = "Mì Ramen Nhật Bản Thịt Heo Chashu",
                Category = "Asia",
                CategoryName = "Ẩm Thực Á Châu",
                Price = 145000,
                Description = "Nước dùng Tonkotsu hầm từ xương ống đậm đà 16 tiếng, mì tươi dai giòn, thịt heo Chashu cuộn mềm tan cùng trứng ngâm tương lòng đào.",
                Icon = "🍜",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "ASIA04",
                Name = "Canh Tom Yum Cung Đình Thái Hải Sản",
                Category = "Asia",
                CategoryName = "Ẩm Thực Á Châu",
                Price = 165000,
                Description = "Hương vị cay chua kích thích vị giác từ lá chanh Kaffir, sả, nấm hương, nước cốt dừa béo bùi và tôm mực tươi ngon.",
                Icon = "🥘",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "ASIA05",
                Name = "Xửng Dimsum Thượng Hạng 5 Món",
                Category = "Asia",
                CategoryName = "Ẩm Thực Á Châu",
                Price = 180000,
                Description = "Gồm há cảo tôm pha lê, xíu mại tôm thịt trứng muối, bánh bao kim sa trứng chảy và há cảo sò điệp hấp trong xửng tre nghi ngút khói.",
                Icon = "🥟",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },

            // ═══ 3. ẨM THỰC ÂU XA XỈ 4 SAO ═══
            new HotelMenuItem
            {
                Id = "EU01",
                Name = "Bò Bít Tết Thăn Ngoại Úc (Australian Ribeye)",
                Category = "Europe",
                CategoryName = "Ẩm Thực Âu 4 Sao",
                Price = 360000,
                Description = "250g thăn bò Úc hảo hạng nướng trên than hồng đạt độ mềm mọng lý tưởng, kèm khoai tây nghiền bơ Pháp Truffle và sốt tiêu đen Phú Quốc.",
                Icon = "🥩",
                ImageUrl = "/images/hotel-assets/menu/menu-wagyu-steak.jpg",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "EU02",
                Name = "Cá Hồi Na Uy Áp Chảo Sốt Bơ Chanh",
                Category = "Europe",
                CategoryName = "Ẩm Thực Âu 4 Sao",
                Price = 290000,
                Description = "Phi lê cá hồi Na Uy áp chảo da giòn rụm bên trong mọng nước, ăn kèm măng tây xanh xào bơ tỏi và sốt bơ chanh vàng kiểu Pháp Beurre Blanc.",
                Icon = "🐟",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "EU03",
                Name = "Mì Ý Hải Sản Tươi Sốt Vang Trắng (Seafood Spaghetti)",
                Category = "Europe",
                CategoryName = "Ẩm Thực Âu 4 Sao",
                Price = 195000,
                Description = "Sợi mì Spaghetti Barilla Al dente xào cùng vẹm xanh New Zealand, tôm sú, mực tươi, sốt vang trắng và cà chua bi Địa Trung Hải.",
                Icon = "🍝",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "EU04",
                Name = "Súp Kem Nấm Bào Ngư Truffle Đen",
                Category = "Europe",
                CategoryName = "Ẩm Thực Âu 4 Sao",
                Price = 135000,
                Description = "Súp kem nhuyễn mịn thơm ngát mùi dầu nấm Truffle đen quý hiếm từ Ý, nấm bào ngư tươi xào bơ, ăn kèm bánh mì bơ tỏi giòn rụm.",
                Icon = "🥣",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "EU05",
                Name = "Pizza Hải Sản Địa Trung Hải 30cm",
                Category = "Europe",
                CategoryName = "Ẩm Thực Âu 4 Sao",
                Price = 220000,
                Description = "Đế bánh nướng củi giòn xốp thủ công, phủ phô mai Mozzarella kéo sợi béo ngậy, tôm tươi, mực giòn, ớt chuông nướng và lá húng quế Ý.",
                Icon = "🍕",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },

            // ═══ 4. ĐỒ UỐNG & QUẦY BAR CAO CẤP ═══
            new HotelMenuItem
            {
                Id = "BAR01",
                Name = "Cà Phê Trứng Hà Nội Đặc Biệt",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 55000,
                Description = "Lớp kem trứng đánh bông mịn như mây béo ngậy, hòa quyện bên dưới là cà phê Robusta đậm đặc thơm nồng đặc trưng Hà Thành.",
                Icon = "☕",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "BAR02",
                Name = "Cà Phê Phin Sữa Đá Cầu Đất",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 45000,
                Description = "Hạt Arabica Cầu Đất nguyên chất pha phin truyền thống thơm quyến rũ, kết hợp sữa đặc ngọt thanh trên đá mát lạnh.",
                Icon = "🧊",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "BAR03",
                Name = "Nước Ép Trái Cây Tươi (Cam / Dưa Hấu / Chanh Dây)",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 50000,
                Description = "100% trái cây nhiệt đới tươi chọn lọc ép lạnh nguyên chất giữ trọn vẹn vitamin, không pha đường hóa học.",
                Icon = "🍹",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "BAR04",
                Name = "Trà Sen Vàng Long Nhãn Cung Đình",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 55000,
                Description = "Trà ô long thượng hạng ướp hương hoa sen thanh khiết, kết hợp cùi long nhãn giòn ngọt và hạt sen Huế bùi dẻo.",
                Icon = "🍵",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "BAR05",
                Name = "Cocktail Mojito Bạc Hà Cổ Điển",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 95000,
                Description = "Rượu Bacardi White Rum hòa quyện nước cốt chanh tươi, lá bạc hà giã tay thơm mát và soda tạo cảm giác sảng khoái tuyệt đối.",
                Icon = "🍸",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "BAR06",
                Name = "Rượu Vang Đỏ Bordeaux Pháp (Ly)",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 150000,
                Description = "Vang đỏ vùng Bordeaux nổi tiếng, màu đỏ ruby sóng sánh, hương vị gỗ sồi và quả mọng chín sâu lắng, tinh tế.",
                Icon = "🍷",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "BAR07",
                Name = "Bia Thủ Công Sun Hotel Golden Ale (Chai)",
                Category = "Beverage",
                CategoryName = "Đồ Uống & Quầy Bar",
                Price = 70000,
                Description = "Bia thủ công ủ men độc quyền dành riêng cho khách sạn Sun Hotel, thơm dịu hương hoa bia cỏ mật và hậu vị ngọt nhẹ.",
                Icon = "🍺",
                ServiceType = ServiceType.Beverage,
                Department = "Bar",
                IsPopular = true
            },

            // ═══ 5. TRÁNG MIỆNG & ĂN NHẸ ═══
            new HotelMenuItem
            {
                Id = "DES01",
                Name = "Bánh Tiramisu Ý Cổ Điển Mascarpone",
                Category = "Dessert",
                CategoryName = "Tráng Miệng & Ăn Nhẹ",
                Price = 75000,
                Description = "Bánh sampa thấm đẫm rượu cà phê Kahlua, lớp kem phô mai Mascarpone béo ngậy phủ bột cacao nguyên chất đắng nhẹ.",
                Icon = "🍰",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = true
            },
            new HotelMenuItem
            {
                Id = "DES02",
                Name = "Chè Hạt Sen Long Nhãn Đường Phèn Huế",
                Category = "Dessert",
                CategoryName = "Tráng Miệng & Ăn Nhẹ",
                Price = 60000,
                Description = "Hạt sen bọc khéo léo trong cùi nhãn Hưng Yên, nấu cùng đường phèn thanh mát bổ dưỡng an thần ngủ ngon.",
                Icon = "🍧",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            },
            new HotelMenuItem
            {
                Id = "DES03",
                Name = "Đĩa Trái Cây Bốn Mùa Khách Sạn Cắt Tỉa",
                Category = "Dessert",
                CategoryName = "Tráng Miệng & Ăn Nhẹ",
                Price = 80000,
                Description = "Đĩa trái cây tươi gồm thanh long ruột đỏ, dưa lưới Nhật, dâu tây Đà Lạt, nho Mỹ được bếp trưởng cắt tỉa nghệ thuật.",
                Icon = "🍉",
                ServiceType = ServiceType.Food,
                Department = "Bếp",
                IsPopular = false
            }
        };
    }
}
