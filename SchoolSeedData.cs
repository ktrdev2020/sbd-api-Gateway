using Microsoft.EntityFrameworkCore;
using SBD.Domain.Entities;
using SBD.Infrastructure.Data;

namespace Gateway;

public static class SchoolSeedData
{
    public static async Task SeedAsync(SbdDbContext db)
    {
        if (await db.Schools.AnyAsync()) return;

        // Seed Country
        var thailand = await db.Countries.FirstOrDefaultAsync(c => c.Code == "TH");
        if (thailand == null)
        {
            thailand = new Country { Code = "TH", NameTh = "ประเทศไทย", NameEn = "Thailand" };
            db.Countries.Add(thailand);
            await db.SaveChangesAsync();
        }

        // Seed Province
        var province = await db.Provinces.FirstOrDefaultAsync(p => p.Code == "33");
        if (province == null)
        {
            province = new Province { Code = "33", NameTh = "ศรีสะเกษ", NameEn = "Si Sa Ket", Region = "ตะวันออกเฉียงเหนือ", CountryId = thailand.Id };
            db.Provinces.Add(province);
            await db.SaveChangesAsync();
        }

        // Seed AreaType
        var areaType = await db.AreaTypes.FirstOrDefaultAsync(a => a.Code == "สพป");
        if (areaType == null)
        {
            areaType = new AreaType { Code = "สพป", NameTh = "สำนักงานเขตพื้นที่การศึกษาประถมศึกษา", NameShortTh = "สพป.", NameEn = "Primary Educational Service Area Office", Level = 1 };
            db.AreaTypes.Add(areaType);
            await db.SaveChangesAsync();
        }

        // Seed Area
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Code == "ssk3");
        if (area == null)
        {
            area = new Area { Code = "ssk3", NameTh = "สพป.ศรีสะเกษ เขต 3", AreaTypeId = areaType.Id, ProvinceId = province.Id };
            db.Areas.Add(area);
            await db.SaveChangesAsync();
        }

        // Seed Districts
        var district0 = await db.Districts.FirstOrDefaultAsync(d => d.NameTh == "ขุขันธ์" && d.ProvinceId == province.Id);
        if (district0 == null)
        {
            district0 = new District { Code = "3305", NameTh = "ขุขันธ์", NameEn = "Khukhan", ProvinceId = province.Id };
            db.Districts.Add(district0);
        }
        var district1 = await db.Districts.FirstOrDefaultAsync(d => d.NameTh == "ปรางค์กู่" && d.ProvinceId == province.Id);
        if (district1 == null)
        {
            district1 = new District { Code = "3307", NameTh = "ปรางค์กู่", NameEn = "Prang Ku", ProvinceId = province.Id };
            db.Districts.Add(district1);
        }
        var district2 = await db.Districts.FirstOrDefaultAsync(d => d.NameTh == "ภูสิงห์" && d.ProvinceId == province.Id);
        if (district2 == null)
        {
            district2 = new District { Code = "3303", NameTh = "ภูสิงห์", NameEn = "Phu Sing", ProvinceId = province.Id };
            db.Districts.Add(district2);
        }
        var district3 = await db.Districts.FirstOrDefaultAsync(d => d.NameTh == "ไพรบึง" && d.ProvinceId == province.Id);
        if (district3 == null)
        {
            district3 = new District { Code = "3306", NameTh = "ไพรบึง", NameEn = "Phrai Bueng", ProvinceId = province.Id };
            db.Districts.Add(district3);
        }
        await db.SaveChangesAsync();

        // Seed SubDistricts
        var sd0 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "กฤษณา" && s.District.NameTh == "ขุขันธ์");
        if (sd0 == null) { sd0 = new SubDistrict { Code = "330501", NameTh = "กฤษณา", NameEn = "กฤษณา", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd0); }
        var sd1 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "กันทรารมย์" && s.District.NameTh == "ขุขันธ์");
        if (sd1 == null) { sd1 = new SubDistrict { Code = "330502", NameTh = "กันทรารมย์", NameEn = "กันทรารมย์", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd1); }
        var sd2 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "โคกเพชร" && s.District.NameTh == "ขุขันธ์");
        if (sd2 == null) { sd2 = new SubDistrict { Code = "330503", NameTh = "โคกเพชร", NameEn = "โคกเพชร", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd2); }
        var sd3 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "จะกง" && s.District.NameTh == "ขุขันธ์");
        if (sd3 == null) { sd3 = new SubDistrict { Code = "330504", NameTh = "จะกง", NameEn = "จะกง", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd3); }
        var sd4 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ใจดี" && s.District.NameTh == "ขุขันธ์");
        if (sd4 == null) { sd4 = new SubDistrict { Code = "330505", NameTh = "ใจดี", NameEn = "ใจดี", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd4); }
        var sd5 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ดองกำเม็ด" && s.District.NameTh == "ขุขันธ์");
        if (sd5 == null) { sd5 = new SubDistrict { Code = "330506", NameTh = "ดองกำเม็ด", NameEn = "ดองกำเม็ด", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd5); }
        var sd6 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ตะเคียน" && s.District.NameTh == "ขุขันธ์");
        if (sd6 == null) { sd6 = new SubDistrict { Code = "330507", NameTh = "ตะเคียน", NameEn = "ตะเคียน", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd6); }
        var sd7 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ตาอุด" && s.District.NameTh == "ขุขันธ์");
        if (sd7 == null) { sd7 = new SubDistrict { Code = "330508", NameTh = "ตาอุด", NameEn = "ตาอุด", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd7); }
        var sd8 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "นิคมพัฒนา" && s.District.NameTh == "ขุขันธ์");
        if (sd8 == null) { sd8 = new SubDistrict { Code = "330509", NameTh = "นิคมพัฒนา", NameEn = "นิคมพัฒนา", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd8); }
        var sd9 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ปราสาท" && s.District.NameTh == "ขุขันธ์");
        if (sd9 == null) { sd9 = new SubDistrict { Code = "330510", NameTh = "ปราสาท", NameEn = "ปราสาท", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd9); }
        var sd10 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ปรือใหญ่" && s.District.NameTh == "ขุขันธ์");
        if (sd10 == null) { sd10 = new SubDistrict { Code = "330511", NameTh = "ปรือใหญ่", NameEn = "ปรือใหญ่", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd10); }
        var sd11 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ลมศักดิ์" && s.District.NameTh == "ขุขันธ์");
        if (sd11 == null) { sd11 = new SubDistrict { Code = "330512", NameTh = "ลมศักดิ์", NameEn = "ลมศักดิ์", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd11); }
        var sd12 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ศรีตระกูล" && s.District.NameTh == "ขุขันธ์");
        if (sd12 == null) { sd12 = new SubDistrict { Code = "330513", NameTh = "ศรีตระกูล", NameEn = "ศรีตระกูล", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd12); }
        var sd13 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ศรีสะอาด" && s.District.NameTh == "ขุขันธ์");
        if (sd13 == null) { sd13 = new SubDistrict { Code = "330514", NameTh = "ศรีสะอาด", NameEn = "ศรีสะอาด", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd13); }
        var sd14 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สะเดาใหญ่" && s.District.NameTh == "ขุขันธ์");
        if (sd14 == null) { sd14 = new SubDistrict { Code = "330515", NameTh = "สะเดาใหญ่", NameEn = "สะเดาใหญ่", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd14); }
        var sd15 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สำโรงตาเจ็น" && s.District.NameTh == "ขุขันธ์");
        if (sd15 == null) { sd15 = new SubDistrict { Code = "330516", NameTh = "สำโรงตาเจ็น", NameEn = "สำโรงตาเจ็น", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd15); }
        var sd16 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "โสน" && s.District.NameTh == "ขุขันธ์");
        if (sd16 == null) { sd16 = new SubDistrict { Code = "330517", NameTh = "โสน", NameEn = "โสน", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd16); }
        var sd17 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "หนองฉลอง" && s.District.NameTh == "ขุขันธ์");
        if (sd17 == null) { sd17 = new SubDistrict { Code = "330518", NameTh = "หนองฉลอง", NameEn = "หนองฉลอง", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd17); }
        var sd18 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ห้วยใต้" && s.District.NameTh == "ขุขันธ์");
        if (sd18 == null) { sd18 = new SubDistrict { Code = "330519", NameTh = "ห้วยใต้", NameEn = "ห้วยใต้", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd18); }
        var sd19 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ห้วยสำราญ" && s.District.NameTh == "ขุขันธ์");
        if (sd19 == null) { sd19 = new SubDistrict { Code = "330520", NameTh = "ห้วยสำราญ", NameEn = "ห้วยสำราญ", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd19); }
        var sd20 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ห้วยเหนือ" && s.District.NameTh == "ขุขันธ์");
        if (sd20 == null) { sd20 = new SubDistrict { Code = "330521", NameTh = "ห้วยเหนือ", NameEn = "ห้วยเหนือ", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd20); }
        var sd21 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "หัวเสือ" && s.District.NameTh == "ขุขันธ์");
        if (sd21 == null) { sd21 = new SubDistrict { Code = "330522", NameTh = "หัวเสือ", NameEn = "หัวเสือ", PostalCode = "33140", DistrictId = district0.Id }; db.SubDistricts.Add(sd21); }
        var sd22 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "กู่" && s.District.NameTh == "ปรางค์กู่");
        if (sd22 == null) { sd22 = new SubDistrict { Code = "330723", NameTh = "กู่", NameEn = "กู่", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd22); }
        var sd23 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ดู่" && s.District.NameTh == "ปรางค์กู่");
        if (sd23 == null) { sd23 = new SubDistrict { Code = "330724", NameTh = "ดู่", NameEn = "ดู่", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd23); }
        var sd24 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ตูม" && s.District.NameTh == "ปรางค์กู่");
        if (sd24 == null) { sd24 = new SubDistrict { Code = "330725", NameTh = "ตูม", NameEn = "ตูม", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd24); }
        var sd25 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "พิมาย" && s.District.NameTh == "ปรางค์กู่");
        if (sd25 == null) { sd25 = new SubDistrict { Code = "330726", NameTh = "พิมาย", NameEn = "พิมาย", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd25); }
        var sd26 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "พิมายเหนือ" && s.District.NameTh == "ปรางค์กู่");
        if (sd26 == null) { sd26 = new SubDistrict { Code = "330727", NameTh = "พิมายเหนือ", NameEn = "พิมายเหนือ", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd26); }
        var sd27 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "โพธิ์ศรี" && s.District.NameTh == "ปรางค์กู่");
        if (sd27 == null) { sd27 = new SubDistrict { Code = "330728", NameTh = "โพธิ์ศรี", NameEn = "โพธิ์ศรี", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd27); }
        var sd28 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สมอ" && s.District.NameTh == "ปรางค์กู่");
        if (sd28 == null) { sd28 = new SubDistrict { Code = "330729", NameTh = "สมอ", NameEn = "สมอ", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd28); }
        var sd29 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สวาย" && s.District.NameTh == "ปรางค์กู่");
        if (sd29 == null) { sd29 = new SubDistrict { Code = "330730", NameTh = "สวาย", NameEn = "สวาย", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd29); }
        var sd30 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สำโรงปราสาท" && s.District.NameTh == "ปรางค์กู่");
        if (sd30 == null) { sd30 = new SubDistrict { Code = "330731", NameTh = "สำโรงปราสาท", NameEn = "สำโรงปราสาท", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd30); }
        var sd31 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "หนองเชียงทูน" && s.District.NameTh == "ปรางค์กู่");
        if (sd31 == null) { sd31 = new SubDistrict { Code = "330732", NameTh = "หนองเชียงทูน", NameEn = "หนองเชียงทูน", PostalCode = "33170", DistrictId = district1.Id }; db.SubDistricts.Add(sd31); }
        var sd32 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ดินแดง" && s.District.NameTh == "ไพรบึง");
        if (sd32 == null) { sd32 = new SubDistrict { Code = "330633", NameTh = "ดินแดง", NameEn = "ดินแดง", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd32); }
        var sd33 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "โนนปูน" && s.District.NameTh == "ไพรบึง");
        if (sd33 == null) { sd33 = new SubDistrict { Code = "330634", NameTh = "โนนปูน", NameEn = "โนนปูน", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd33); }
        var sd34 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ปราสาทเยอ" && s.District.NameTh == "ไพรบึง");
        if (sd34 == null) { sd34 = new SubDistrict { Code = "330635", NameTh = "ปราสาทเยอ", NameEn = "ปราสาทเยอ", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd34); }
        var sd35 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ไพรบึง" && s.District.NameTh == "ไพรบึง");
        if (sd35 == null) { sd35 = new SubDistrict { Code = "330636", NameTh = "ไพรบึง", NameEn = "ไพรบึง", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd35); }
        var sd36 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สำโรงพลัน" && s.District.NameTh == "ไพรบึง");
        if (sd36 == null) { sd36 = new SubDistrict { Code = "330637", NameTh = "สำโรงพลัน", NameEn = "สำโรงพลัน", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd36); }
        var sd37 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "สุขสวัสดิ์" && s.District.NameTh == "ไพรบึง");
        if (sd37 == null) { sd37 = new SubDistrict { Code = "330638", NameTh = "สุขสวัสดิ์", NameEn = "สุขสวัสดิ์", PostalCode = "33180", DistrictId = district3.Id }; db.SubDistricts.Add(sd37); }
        var sd38 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "โคกตาล" && s.District.NameTh == "ภูสิงห์");
        if (sd38 == null) { sd38 = new SubDistrict { Code = "330339", NameTh = "โคกตาล", NameEn = "โคกตาล", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd38); }
        var sd39 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ดงรัก" && s.District.NameTh == "ภูสิงห์");
        if (sd39 == null) { sd39 = new SubDistrict { Code = "330340", NameTh = "ดงรัก", NameEn = "ดงรัก", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd39); }
        var sd40 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ตะเคียนราม" && s.District.NameTh == "ภูสิงห์");
        if (sd40 == null) { sd40 = new SubDistrict { Code = "330341", NameTh = "ตะเคียนราม", NameEn = "ตะเคียนราม", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd40); }
        var sd41 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ไพรพัฒนา" && s.District.NameTh == "ภูสิงห์");
        if (sd41 == null) { sd41 = new SubDistrict { Code = "330342", NameTh = "ไพรพัฒนา", NameEn = "ไพรพัฒนา", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd41); }
        var sd42 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ละลม" && s.District.NameTh == "ภูสิงห์");
        if (sd42 == null) { sd42 = new SubDistrict { Code = "330343", NameTh = "ละลม", NameEn = "ละลม", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd42); }
        var sd43 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ห้วยตามอญ" && s.District.NameTh == "ภูสิงห์");
        if (sd43 == null) { sd43 = new SubDistrict { Code = "330344", NameTh = "ห้วยตามอญ", NameEn = "ห้วยตามอญ", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd43); }
        var sd44 = await db.SubDistricts.FirstOrDefaultAsync(s => s.NameTh == "ห้วยตึ๊กชู" && s.District.NameTh == "ภูสิงห์");
        if (sd44 == null) { sd44 = new SubDistrict { Code = "330345", NameTh = "ห้วยตึ๊กชู", NameEn = "ห้วยตึ๊กชู", PostalCode = "33140", DistrictId = district2.Id }; db.SubDistricts.Add(sd44); }
        await db.SaveChangesAsync();

        // Build subdistrict lookup
        var sdLookup = new Dictionary<string, int>
        {
            ["กฤษณา_ขุขันธ์"] = sd0.Id,
            ["กันทรารมย์_ขุขันธ์"] = sd1.Id,
            ["โคกเพชร_ขุขันธ์"] = sd2.Id,
            ["จะกง_ขุขันธ์"] = sd3.Id,
            ["ใจดี_ขุขันธ์"] = sd4.Id,
            ["ดองกำเม็ด_ขุขันธ์"] = sd5.Id,
            ["ตะเคียน_ขุขันธ์"] = sd6.Id,
            ["ตาอุด_ขุขันธ์"] = sd7.Id,
            ["นิคมพัฒนา_ขุขันธ์"] = sd8.Id,
            ["ปราสาท_ขุขันธ์"] = sd9.Id,
            ["ปรือใหญ่_ขุขันธ์"] = sd10.Id,
            ["ลมศักดิ์_ขุขันธ์"] = sd11.Id,
            ["ศรีตระกูล_ขุขันธ์"] = sd12.Id,
            ["ศรีสะอาด_ขุขันธ์"] = sd13.Id,
            ["สะเดาใหญ่_ขุขันธ์"] = sd14.Id,
            ["สำโรงตาเจ็น_ขุขันธ์"] = sd15.Id,
            ["โสน_ขุขันธ์"] = sd16.Id,
            ["หนองฉลอง_ขุขันธ์"] = sd17.Id,
            ["ห้วยใต้_ขุขันธ์"] = sd18.Id,
            ["ห้วยสำราญ_ขุขันธ์"] = sd19.Id,
            ["ห้วยเหนือ_ขุขันธ์"] = sd20.Id,
            ["หัวเสือ_ขุขันธ์"] = sd21.Id,
            ["กู่_ปรางค์กู่"] = sd22.Id,
            ["ดู่_ปรางค์กู่"] = sd23.Id,
            ["ตูม_ปรางค์กู่"] = sd24.Id,
            ["พิมาย_ปรางค์กู่"] = sd25.Id,
            ["พิมายเหนือ_ปรางค์กู่"] = sd26.Id,
            ["โพธิ์ศรี_ปรางค์กู่"] = sd27.Id,
            ["สมอ_ปรางค์กู่"] = sd28.Id,
            ["สวาย_ปรางค์กู่"] = sd29.Id,
            ["สำโรงปราสาท_ปรางค์กู่"] = sd30.Id,
            ["หนองเชียงทูน_ปรางค์กู่"] = sd31.Id,
            ["ดินแดง_ไพรบึง"] = sd32.Id,
            ["โนนปูน_ไพรบึง"] = sd33.Id,
            ["ปราสาทเยอ_ไพรบึง"] = sd34.Id,
            ["ไพรบึง_ไพรบึง"] = sd35.Id,
            ["สำโรงพลัน_ไพรบึง"] = sd36.Id,
            ["สุขสวัสดิ์_ไพรบึง"] = sd37.Id,
            ["โคกตาล_ภูสิงห์"] = sd38.Id,
            ["ดงรัก_ภูสิงห์"] = sd39.Id,
            ["ตะเคียนราม_ภูสิงห์"] = sd40.Id,
            ["ไพรพัฒนา_ภูสิงห์"] = sd41.Id,
            ["ละลม_ภูสิงห์"] = sd42.Id,
            ["ห้วยตามอญ_ภูสิงห์"] = sd43.Id,
            ["ห้วยตึ๊กชู_ภูสิงห์"] = sd44.Id,
        };

        // Seed 196 Schools from สพป.ศรีสะเกษ เขต 3
        var schoolsData = new (string Code, string Name, string? Principal, DateOnly? EstablishedDate, string? TaxId, string? Level, string? Type, string Tambon, string Amphoe, string PostalCode, string? Phone, string? Phone2)[]
        {
            ("1033530251", "บ้านกฤษณา", "นายประสิทธิ์ ชมภูเขา", new DateOnly(1925, 10, 29), null, "1", null, "กฤษณา", "ขุขันธ์", "33140", "0973284258", null),
            ("1033530252", "สวัสดีวิทยา", "นายพิชิต วงศ์เพชรชัย", new DateOnly(1939, 7, 21), null, "13", null, "กฤษณา", "ขุขันธ์", "33140", null, null),
            ("1033530253", "บ้านป่าใต้", "นายธนวัฒน์ ปรทานัง", new DateOnly(1942, 2, 1), null, "7", null, "กฤษณา", "ขุขันธ์", "33140", null, null),
            ("1033530238", "บ้านกันทรารมย์", "นายอุกฤต ทีงาม", new DateOnly(1923, 5, 27), "3305000001", "3", null, "กันทรารมย์", "ขุขันธ์", "33140", "0652402751", "0819666137"),
            ("1033530239", "บ้านโคกสูง", "นางสาวนิตยาภรณ์ ตรีแก้ว", new DateOnly(1939, 6, 12), null, "4", null, "กันทรารมย์", "ขุขันธ์", "33140", "045-920615", null),
            ("1033530267", "บ้านระกา", "นางวิลาสินี คำวงค์", new DateOnly(1938, 7, 1), null, "5", null, "โคกเพชร", "ขุขันธ์", "33140", "0819760832", null),
            ("1033530268", "บ้านโคกเพชร", "นางกังสดาล มนต์ฤทธานุภาพ", new DateOnly(1957, 10, 15), null, "1", "โคกเพชร", "โคกเพชร", "ขุขันธ์", "33140", null, null),
            ("1033530269", "บ้านเปี่ยมตะลวก", "นางดาวรุ่ง ปลื้มใจ", new DateOnly(1948, 6, 24), null, "2", null, "โคกเพชร", "ขุขันธ์", "33140", "0924793639", "0801867569"),
            ("1033530260", "บ้านภูมิศาลา", "นายกิจจา เตารัตน์", new DateOnly(1951, 6, 24), null, "4", "ปรางค์กู่ - ศรีสะเกษ", "โคกเพชร", "ขุขันธ์", "33140", "0892820614", "0833647416"),
            ("1033530262", "บ้านเสลาสุขเกษม", "นางไพลิน บัวงาม", new DateOnly(1939, 5, 22), "33050317761", "6", "แทรง-ประปุน", "โคกเพชร", "ขุขันธ์", "33140", "0819663681", "0862499752"),
            ("1033530254", "บ้านปะอุง", null, new DateOnly(1963, 4, 22), null, "6", null, "จะกง", "ขุขันธ์", "33140", null, "0909909939"),
            ("1033530255", "บ้านเค็ง", "นายสุริยน บุญเหมาะ", new DateOnly(1972, 8, 12), null, "7", null, "จะกง", "ขุขันธ์", "33140", "045660366", "0856587772"),
            ("1033530250", "บ้านตาสุด", "ว่าที่ ร.ต.สมคิด นามบุตร", new DateOnly(1939, 11, 24), null, "13", null, "จะกง", "ขุขันธ์", "33140", null, null),
            ("1033530249", "จะกงวิทยา", "นายอภัยสิทธิ์ ใจพร", new DateOnly(1925, 11, 25), "33050497173", "8", null, "จะกง", "ขุขันธ์", "33140", "0879942653", null),
            ("1033530256", "บ้านใจดี", "นายบรรลือ สุนทร", new DateOnly(1923, 10, 10), null, "1", null, "ใจดี", "ขุขันธ์", "33140", null, null),
            ("1033530257", "บ้านทะลอก", "นายสมพร พงษ์เพชร", new DateOnly(1938, 9, 1), null, "3", null, "ใจดี", "ขุขันธ์", "33140", "0856354049", null),
            ("1033530258", "บ้านพะเยียวตาสุ(อสพป.37)", "นางขวัญชนก ดวงพิมพ์", new DateOnly(1962, 4, 23), null, "2", null, "ใจดี", "ขุขันธ์", "33140", "0844748702", "0826737031"),
            ("1033530259", "บ้านอังกุล", "นายศุภโชค สำราญล้ำ", new DateOnly(1956, 7, 10), null, "7", null, "ใจดี", "ขุขันธ์", "33140", "0814693549", null),
            ("1033530265", "บ้านกระโพธิ์ช่างหม้อ", "นายสมปอง ออไธสง", new DateOnly(1939, 11, 27), null, "6", null, "ดองกำเม็ด", "ขุขันธ์", "33140", null, null),
            ("1033530266", "บ้านตรางสวาย", "นางบานชื่น อินอุเทน", new DateOnly(1957, 10, 28), null, "4", "ขุขันธ์ - ศรีสะเกษ", "ดองกำเม็ด", "ขุขันธ์", "33140", "0823916515", "0823718154"),
            ("1033530263", "บ้านดองกำเม็ด", "นายไพรวัลย์ จันทเสน", new DateOnly(1934, 8, 28), null, "5", "ขุขันธ์ - ศรีสะเกษ", "ดองกำเม็ด", "ขุขันธ์", "33140", "045826525", null),
            ("1033530264", "บ้านกันจาน", "นายวีระพงศ์ ศรีอินทร์", new DateOnly(1939, 5, 1), null, "1", "ขุขันธ์-ศรีสะเกษ", "ดองกำเม็ด", "ขุขันธ์", "33140", "045685122", null),
            ("1033530270", "บ้านตะเคียนช่างเหล็ก", "นายพีรณัฐ วันดีรัตน์", new DateOnly(1925, 6, 10), null, "1", null, "ตะเคียน", "ขุขันธ์", "33140", null, null),
            ("1033530272", "บ้านกะกำ", "นายบุตรดา จันทเสน", new DateOnly(1938, 5, 27), "33050317906", "4", null, "ตะเคียน", "ขุขันธ์", "33140", "0872399927", null),
            ("1033530275", "บ้านบัวบก", "นางบุญล้อม โยธี", new DateOnly(1980, 4, 1), null, "3", null, "ตะเคียน", "ขุขันธ์", "33140", "0985980956", null),
            ("1033530290", "บ้านตาอุด", "นายสิรวิชญ์ ภูติยา", new DateOnly(1920, 5, 8), null, "1", null, "ตาอุด", "ขุขันธ์", "33140", "045922236", null),
            ("1033530311", "บ้านปราสาทกวางขาว", "นายจิรายุ หินจำปา", new DateOnly(1924, 11, 24), null, "4", null, "นิคมพัฒนา", "ขุขันธ์", "33140", "0635915469", null),
            ("1033530282", "นิคม ๑", "นายเด่นพงษ์ ไพศาลสุวรรณ", new DateOnly(1959, 6, 19), null, "10", null, "นิคมพัฒนา", "ขุขันธ์", "33140", "0884798828", null),
            ("1033530283", "นิคม ๒ (ตชด.สงเคราะห์)", "นางสาวรำไพพรรณ เครือชัย", new DateOnly(1959, 11, 12), null, "3", null, "นิคมพัฒนา", "ขุขันธ์", "33140", "0618954164", null),
            ("1033530241", "บ้านปราสาท", "นายเดชา จันดาบุตร", new DateOnly(1934, 9, 15), null, "2", null, "ปราสาท", "ขุขันธ์", "33140", null, null),
            ("1033530243", "บ้านหนองสะแกสน", "นางปิยนาถ บุญเย็น", new DateOnly(1960, 6, 1), null, "5", null, "ปราสาท", "ขุขันธ์", "33140", "0619419941", null),
            ("1033530244", "บ้านบ่อทอง", "นายธีรศักดิ์ พฤกษา", new DateOnly(1962, 4, 23), null, "7", null, "ปราสาท", "ขุขันธ์", "33140", null, null),
            ("1033530245", "บ้านสกุล", "นางเข็มเพชร ประดับศรี", new DateOnly(1970, 5, 27), null, "6", null, "ปราสาท", "ขุขันธ์", "33140", "085-4159939", "098-3486193"),
            ("1033530247", "บ้านคลองเพชรสวาย", "นายสมาน พุทธวงค์", new DateOnly(1977, 5, 9), "3305-043268-3", "4", null, "ปราสาท", "ขุขันธ์", "33140", "045660189", null),
            ("1033530248", "บ้านกันโทรกประชาสรรค์", "นางสาวลักษณารีย์ สมเสนา", new DateOnly(1997, 1, 19), "3305-041788-9", "3", null, "ปราสาท", "ขุขันธ์", "33140", "045660010", "0801572348"),
            ("1033530277", "บ้านปรือคัน", "นายไพศาล พันธ์ภักดี", new DateOnly(1928, 5, 12), null, "14", "ขุขันธ์-โคกตาล", "ปรือใหญ่", "ขุขันธ์", "33140", "0967402105", null),
            ("1033530278", "บ้านปรือใหญ่", "นายภัทรศักดิ์ มนทอง", new DateOnly(1937, 9, 27), null, "1", null, "ปรือใหญ่", "ขุขันธ์", "33140", null, null),
            ("1033530279", "บ้านมะขาม", "นายประวิทย์ อยู่ยืน", new DateOnly(1961, 6, 12), null, "4", null, "ปรือใหญ่", "ขุขันธ์", "33140", "0828623952", null),
            ("1033530280", "บ้านหลัก", "นายแสงอรุณ ดวงใจ", new DateOnly(1940, 8, 1), null, "2", null, "ปรือใหญ่", "ขุขันธ์", "33140", "093-4914441", "0862624745"),
            ("1033530281", "บ้านโนนสมบูรณ์(อสพป.25)", "นางผการัตน์ วงศ์ชัยภูมิ", new DateOnly(1972, 8, 15), null, "18", null, "ปรือใหญ่", "ขุขันธ์", "33140", "045660353", "0987742219"),
            ("1033530284", "ทับทิมสยาม ๐๖", "นางสาวน้ำฝน จันทะสนธ์", new DateOnly(1992, 4, 8), null, "13", null, "ปรือใหญ่", "ขุขันธ์", "33140", "0996530935", null),
            ("1033530274", "บ้านทุ่งศักดิ์", "นางสาวเนตรฤดี ตะเคียนจันทร์", new DateOnly(1971, 4, 1), null, "1", null, "ลมศักดิ์", "ขุขันธ์", "33140", null, null),
            ("1033530273", "บ้านหนองกาด", "นางศุภวรรณ จิตต์ภักดี", new DateOnly(1961, 6, 12), null, "5", null, "ลมศักดิ์", "ขุขันธ์", "33140", "0644491214", "0612508404"),
            ("1033530271", "บ้านจันลม", "นายจำนาน ไพบูลย์", new DateOnly(1934, 9, 3), null, "8", null, "ลมศักดิ์", "ขุขันธ์", "33140", null, null),
            ("1033530292", "บ้านโนนใหม่(ประชาอุปถัมภ์)", "นายธนพล สาระไทย", new DateOnly(1962, 4, 23), "3305-035291-4", "6", null, "ศรีตระกูล", "ขุขันธ์", "33140", "045660352", null),
            ("1033530293", "บ้านเคาะสนวนสามัคคี", "นางกิตติยา เจริญศิลป์", new DateOnly(1938, 7, 1), null, "7", null, "ศรีตระกูล", "ขุขันธ์", "33140", "0964901475", null),
            ("1033530294", "บ้านละเบิกตาฮีง", null, new DateOnly(1971, 5, 16), null, "4", null, "ศรีตระกูล", "ขุขันธ์", "33140", null, null),
            ("1033530246", "บ้านคล้อโคกกว้าง", "นายวินัย ประทุมวัน", new DateOnly(1971, 5, 15), null, "3", null, "ศรีสะอาด", "ขุขันธ์", "33140", null, null),
            ("1033530240", "ศรีสะอาดวิทยาคม", "นายสุรพงษ์ เวชพันธ์", new DateOnly(2002, 11, 29), null, "6", null, "ศรีสะอาด", "ขุขันธ์", "33140", null, null),
            ("1033530242", "บ้านตะเคียนบังอีง", "นางบุหงา ไชยศรีษะ", new DateOnly(1953, 12, 15), "3305", "8", null, "ศรีสะอาด", "ขุขันธ์", "33140", "045660441", null),
            ("1033530289", "บ้านใหม่ประชาสามัคคี", "นางสุนิษา งามรูป", new DateOnly(1972, 8, 9), null, "2", null, "สะเดาใหญ่", "ขุขันธ์", "33140", null, null),
            ("1033530285", "บ้านสะเดาใหญ่", "นายอนันตชัย วงษ์พิทักษ์", new DateOnly(1925, 6, 30), null, "6", null, "สะเดาใหญ่", "ขุขันธ์", "33140", "0956705688", null),
            ("1033530286", "บ้านติม(พันธ์พิทยาคม)", "นายอนุสรณ์ จัตตุเรศ", new DateOnly(1937, 5, 3), null, "15", "ขุขันธ์ - สำโรงพลัน", "สะเดาใหญ่", "ขุขันธ์", "33140", null, null),
            ("1033530287", "บ้านเขวิก", "นายบุญเรือง เสนาะศัพย์", new DateOnly(1961, 4, 12), null, "14", null, "สะเดาใหญ่", "ขุขันธ์", "33140", "045660457", null),
            ("1033530288", "บ้านโพง", "นางสุบรรณ์ สีลาบา", new DateOnly(1934, 9, 1), null, "12", null, "สะเดาใหญ่", "ขุขันธ์", "33140", "0812663732", "045660347"),
            ("1033530310", "บ้านสระบานสามัคคี", "นายนิเวศน์ คำผง", new DateOnly(1956, 7, 15), null, "5", null, "สำโรงตาเจ็น", "ขุขันธ์", "33140", "0892837557", null),
            ("1033530303", "บ้านสำโรงตาเจ็น", "นางทิพรัตน์ คำแพง", new DateOnly(1934, 5, 30), null, "1", null, "สำโรงตาเจ็น", "ขุขันธ์", "33140", null, null),
            ("1033530306", "บ้านศาลาประปุน", "นายสำเริง สอนพูด", new DateOnly(1939, 6, 12), null, "17", null, "สำโรงตาเจ็น", "ขุขันธ์", "33140", "0892860289", null),
            ("1033530307", "บ้านโนนดู่", "นายอำนวย คำแพง", new DateOnly(1940, 7, 1), null, "3", null, "สำโรงตาเจ็น", "ขุขันธ์", "33140", "045969188", null),
            ("1033530308", "บ้านเริงรมย์", null, new DateOnly(1946, 4, 16), null, "10", null, "สำโรงตาเจ็น", "ขุขันธ์", "33140", null, null),
            ("1033530295", "บ้านโสน", "นายธนวรัท คำเสียง", new DateOnly(1925, 6, 15), null, "18", null, "โสน", "ขุขันธ์", "33140", "045969195", null),
            ("1033530296", "บ้านอาวอย", "นายโอภาส กุลัพบุรี", new DateOnly(1941, 5, 26), null, "2", null, "โสน", "ขุขันธ์", "33140", "045660342", null),
            ("1033530297", "บ้านขนุน(วันธรรมศาสตร์ศรีสะเกษ 2515)", "นายสังวาล คำเหลือ", new DateOnly(1938, 6, 12), null, "15", "โชคชัย-เดชอุดม", "โสน", "ขุขันธ์", "33140", "045660370", null),
            ("1033530298", "บ้านสวาย", "นายยุทธนา นิตยวรรณ", new DateOnly(1974, 5, 23), null, "7", null, "โสน", "ขุขันธ์", "33140", "045660258", null),
            ("1033530299", "บ้านหนองคล้า", "นายยิ่งศักดิ์ พลภักดี", new DateOnly(1959, 7, 20), null, "3", null, "โสน", "ขุขันธ์", "33140", null, null),
            ("1033530300", "บ้านคำเผือ", "นางสาวภรณ์สิภัค สุวะพัฒน์", new DateOnly(1961, 5, 14), null, "5", null, "โสน", "ขุขันธ์", "33140", "045969192", "0610780212"),
            ("1033530301", "บ้านสนามสามัคคีสโมสรโรตารี่ 2", "นายพงษ์สุวัฒน์ นามโคตร", new DateOnly(1969, 5, 1), null, "6", null, "โสน", "ขุขันธ์", "33140", null, null),
            ("1033530316", "นิคม 3(กรมประชาสงเคราะห์)", "นายรัตน์นิกรณ์ อ่อนคำ", new DateOnly(1961, 6, 12), null, "6", "ขุขันธ์ - โคกตาล", "หนองฉลอง", "ขุขันธ์", "33140", "0933215213", "0910183586"),
            ("1033530317", "นิคม ๔ (กรมประชาสงเคราะห์)", "นายสุริยะ สมพร", new DateOnly(1965, 9, 1), null, "2", null, "หนองฉลอง", "ขุขันธ์", "33140", "045660257", null),
            ("1033530291", "บ้านตรอย", "นางศิริรัตน์ ชินบุตร", new DateOnly(1941, 5, 26), null, "5", null, "หนองฉลอง", "ขุขันธ์", "33140", "0878549055", "0810623914"),
            ("1033530314", "บ้านตาดม", "นายพิทักษ์พงศ์ วิริยะกาญจนา (รักษาราชการในตำแหน่งผู้อำนวยการโรงเรียน)", new DateOnly(1940, 7, 1), null, "8", null, "ห้วยใต้", "ขุขันธ์", "33140", "0827514958", null),
            ("1033530315", "บ้านสมบูรณ์", "นายมนตรี บุดดี", new DateOnly(1979, 11, 30), null, "4", null, "ห้วยใต้", "ขุขันธ์", "33140", "0933392924", "0870991883"),
            ("1033530312", "บ้านกันแตสระรุน", null, new DateOnly(1971, 4, 30), null, "7", "โชคชัย- เดชอุดม", "ห้วยใต้", "ขุขันธ์", "33140", "045660009", null),
            ("1033530313", "บ้านแขว", "นางนภาพร โคตมา", new DateOnly(1937, 10, 20), null, "9", null, "ห้วยใต้", "ขุขันธ์", "33140", null, null),
            ("1033530326", "บ้านคะนาสามัคคี", "นายสุรงค์ โพชนิกร", new DateOnly(1970, 5, 1), null, "4", null, "ห้วยสำราญ", "ขุขันธ์", "33140", "045-660367", null),
            ("1033530327", "บ้านยางชุมภูมิตำรวจ", "นายสำเริง บุตราช", new DateOnly(1977, 4, 8), "33050201266", "8", null, "ห้วยสำราญ", "ขุขันธ์", "33140", "0826158191", null),
            ("1033530321", "บ้านแทรง", "นางโชติกาญจน์ รศพล", new DateOnly(1937, 7, 1), null, "1", "ศรีสะเกษ-ขุขันธ์", "ห้วยสำราญ", "ขุขันธ์", "33140", "045671729", null),
            ("1033530325", "บ้านนาก๊อก", "นายสสิธร หนันดูน", new DateOnly(1938, 7, 5), null, "5", null, "ห้วยสำราญ", "ขุขันธ์", "33140", null, null),
            ("1033530318", "อนุบาลศรีประชานุกูล", "นายเอกอมร ใจจง", new DateOnly(1917, 12, 24), "3395-000483-1", "6", "ศรีประชานุกูล", "ห้วยเหนือ", "ขุขันธ์", "33140", "045671010", null),
            ("1033530319", "ขุขันธ์วิทยา", "นางอารีรัตน์ พันธ์แก่น", new DateOnly(1956, 5, 17), null, "14", "เทพนิมิต", "ห้วยเหนือ", "ขุขันธ์", "33140", "045671011", null),
            ("1033530320", "บ้านชำแระกลาง", "นายวสันต์ นามวงศ์", new DateOnly(1938, 7, 1), null, "10", null, "ห้วยเหนือ", "ขุขันธ์", "33140", "045-969205", null),
            ("1033530323", "บ้านสะอาง(ประชาสามัคคี)", "นางวรวรรณ มิถุนดี", new DateOnly(1939, 7, 10), null, "12", null, "ห้วยเหนือ", "ขุขันธ์", "33140", "0874470202", "0872496387"),
            ("1033530324", "วัดเขียน", "นายจำรูญ มลิพันธ์", new DateOnly(1957, 10, 25), null, "4", null, "ห้วยเหนือ", "ขุขันธ์", "33140", "094-2843102", "0978750091"),
            ("1033530276", "บ้านเรียม", "นายไพศาล บุญขาว", new DateOnly(1973, 6, 16), "33940000019", "5", null, "ห้วยเหนือ", "ขุขันธ์", "33140", "045660072", null),
            ("1033530309", "บ้านคลองสุด(ประชาอุทิศ)", "นายสิทธิพร สมยิ่ง", new DateOnly(1977, 5, 20), "33030087", "10", null, "หัวเสือ", "ขุขันธ์", "33140", "045660368", null),
            ("1033530302", "ชุมชนบ้านหัวเสือ", "นางสาวพุธิตา คำเหลือ", new DateOnly(1925, 6, 12), null, "1", null, "หัวเสือ", "ขุขันธ์", "33140", null, null),
            ("1033530304", "สวงษ์พัฒนศึกษา", "นายประดิษฐ์ เนียมศรี", new DateOnly(1939, 5, 22), null, "8", null, "หัวเสือ", "ขุขันธ์", "33140", null, null),
            ("1033530305", "บ้านห้วยสระภูมิ", "นางสาวสุภัทรา วาที", new DateOnly(1939, 6, 12), null, "3", null, "หัวเสือ", "ขุขันธ์", "33140", "045685109", "063-6432261"),
            ("1033530586", "บ้านดองดึง", "นางทองอินทร์ สาริก", new DateOnly(1956, 7, 20), null, "4", null, "ดินแดง", "ไพรบึง", "33180", "09859800956", "0956473591"),
            ("1033530587", "บ้านดินแดง", "นายธนชัย กัณทพันธ์", new DateOnly(1939, 11, 28), null, "5", null, "ดินแดง", "ไพรบึง", "33180", "045969338", null),
            ("1033530588", "บ้านสร้างใหญ่", "นายสิริวัฒก์ ดวงสินธ์นิธิกุล", new DateOnly(1946, 7, 10), null, "8", "จันลมหนองแคน", "ดินแดง", "ไพรบึง", "33180", "0624491935", "0624491935"),
            ("1033530589", "หนองอารีพิทยา", "นายธีรชาติ พานิช", new DateOnly(1920, 6, 7), "33060148414", "3", "บ้านลาวเดิม", "ดินแดง", "ไพรบึง", "33180", "045960118", "0862615604"),
            ("1033530590", "บ้านกันตรวจ", "นางสาวชัญธมน สมพงษ์", new DateOnly(1939, 7, 22), null, "1", null, "โนนปูน", "ไพรบึง", "33180", "0846988263", "0899281198"),
            ("1033530591", "บ้านหนองระเยียว", "นายนิพนธ์ สารชาติ", new DateOnly(1941, 2, 1), null, "2", null, "โนนปูน", "ไพรบึง", "33180", "045660311", null),
            ("1033530592", "บ้านตาเจา", "นางสมพรทิพย์ ศิริโท", new DateOnly(1961, 6, 12), null, "3", null, "โนนปูน", "ไพรบึง", "33180", "045660320", null),
            ("1033530593", "บ้านแดง", "นายสุทธิชัย พานิช", new DateOnly(1938, 11, 29), null, "5", null, "โนนปูน", "ไพรบึง", "33180", null, null),
            ("1033530579", "บ้านคูสี่แจ", "นางสาวดรุณี กิมหวล", new DateOnly(1939, 7, 1), null, "7", "คูสี่แจ - หนองอารี", "ปราสาทเยอ", "ไพรบึง", "33180", "0918321778", null),
            ("1033530578", "บ้านปราสาทเยอ", null, new DateOnly(1921, 1, 4), null, "2", "พยุห์-ขุนหาญ", "ปราสาทเยอ", "ไพรบึง", "33180", "0896243904", null),
            ("1033530574", "บ้านหนองพัง", null, new DateOnly(1947, 6, 5), null, "4", null, "ปราสาทเยอ", "ไพรบึง", "33180", null, null),
            ("1033530575", "วัดบ้านประอาง(ประสาธน์คุรุราษฎร์พัฒนา)", "นางสาวนวลแข ปฏิสัมพิทา", new DateOnly(1911, 12, 1), null, "3", "พยุห์ - ขุญหาญ", "ปราสาทเยอ", "ไพรบึง", "33180", null, null),
            ("1033530559", "บ้านติ้ว", "นายเสถียร มนทอง", new DateOnly(1962, 3, 23), null, "6", null, "ไพรบึง", "ไพรบึง", "33180", "0872418298", null),
            ("1033530560", "บ้านมะขามภูมิ", "นายสมคิด ศิริโท", new DateOnly(1938, 5, 31), null, "14", null, "ไพรบึง", "ไพรบึง", "33180", "0892841100", "0883453739"),
            ("1033530561", "บ้านทุ่ม(อ.ส.พ.ป.3)", "นางดวงฤดี มาสอน", new DateOnly(1972, 6, 24), null, "11", null, "ไพรบึง", "ไพรบึง", "33180", "0989496129", "0895831995"),
            ("1033530562", "บ้านกระแมด", "นางศศิภา ต่อพิทักษ์พงศ์", new DateOnly(1973, 5, 1), null, "12", null, "ไพรบึง", "ไพรบึง", "33180", "0839962456", null),
            ("1033530563", "บ้านโพงกอก", "นางณัฐชานัน ศิริชนะ", null, null, "4", null, "ไพรบึง", "ไพรบึง", "33180", "045660315", null),
            ("1033530553", "อนุบาลไพรบึง", "นายสมนึก พันธ์แก่น", new DateOnly(1918, 7, 15), null, "16", "พยุห์ - ขุนหาญ", "ไพรบึง", "ไพรบึง", "33180", null, null),
            ("1033530554", "บ้านสวาย-สนวน", "นายโอภาส สุมาลี", new DateOnly(1955, 5, 10), "132", "2", "พยุห์-ขุนหาญ", "ไพรบึง", "ไพรบึง", "33180", "0899469798", null),
            ("1033530555", "บ้านคอกหนองไพร", "นายทักษิณ อายุวงค์", new DateOnly(1939, 5, 10), null, "1", null, "ไพรบึง", "ไพรบึง", "33180", "0873213129", null),
            ("1033530556", "บ้านตราด", "นางกัลยาฉัตร เธียรทองอินทร์", new DateOnly(1961, 6, 12), null, "10", null, "ไพรบึง", "ไพรบึง", "33180", "0928981551", "0908299380"),
            ("1033530557", "บ้านพราน", "นางกลิ่นแก้ว จันทร์บุญ", new DateOnly(1939, 6, 13), null, "7", null, "ไพรบึง", "ไพรบึง", "33180", "045660140", null),
            ("1033530558", "บ้านผือพอก", "นายธีรกฤษฎิ์ จันทร์บุญ", new DateOnly(1941, 2, 1), null, "9", "ถนนจังกระดาน-พิงพวย", "ไพรบึง", "ไพรบึง", "33180", "045960116", null),
            ("1033530564", "บ้านสำโรงพลัน", "นายอนันต์ ทิพย์รักษ์", new DateOnly(1934, 4, 1), null, "15", null, "สำโรงพลัน", "ไพรบึง", "33180", "045673239", null),
            ("1033530565", "บ้านไม้แก่น", "นายจิรศักดิ์ วงศ์จอม", new DateOnly(1973, 5, 15), null, "12", null, "สำโรงพลัน", "ไพรบึง", "33180", null, "0981023540"),
            ("1033530566", "บ้านปุดเนียม", "นายไพศาล คำศรี", new DateOnly(1970, 5, 1), "3306-009914-6", "7", null, "สำโรงพลัน", "ไพรบึง", "33180", "081-5499068", "086-2448108"),
            ("1033530567", "บ้านหัวช้าง", "นางกาญจนา คำเกิด ครู รก.การในตำแหน่ง ผอ.", new DateOnly(1969, 5, 1), null, "5", "โชคชัย-เดชอุดม", "สำโรงพลัน", "ไพรบึง", "33180", null, null),
            ("1033530569", "บ้านชำแระ", "นายสุทัศน์ แก้วกัณหา", new DateOnly(1938, 7, 1), null, "4", "โชคชัยเดชอุดม", "สำโรงพลัน", "ไพรบึง", "33180", "045969348", "0956209183"),
            ("1033530570", "บ้านตาจวน", "นางสาวเกตุกาญจน์ ชนะดวงใจ", new DateOnly(1979, 5, 22), null, "10", null, "สำโรงพลัน", "ไพรบึง", "33180", null, null),
            ("1033530571", "บ้านไทร", "นายอุดม คำหล่า", new DateOnly(1925, 2, 2), null, "8", null, "สำโรงพลัน", "ไพรบึง", "33180", "0956185404", null),
            ("1033530572", "บ้านสะเดาน้อย", "นายภานุวัฒน์ บุตะมี", new DateOnly(1980, 5, 12), null, "3", "โชคชัย-เดชอุดม กม 238-239", "สำโรงพลัน", "ไพรบึง", "33180", null, "0898653635"),
            ("1033530573", "บ้านโป่ง", "นายสุริยา เกษอินทร์", new DateOnly(1976, 5, 1), null, "2", "โชคชัย - เดชอุดม", "สำโรงพลัน", "ไพรบึง", "33180", "0924919653", null),
            ("1033530582", "บ้านหนองอิไทย", "นายจารึก เข็มทอง", new DateOnly(1938, 1, 1), null, "4", null, "สุขสวัสดิ์", "ไพรบึง", "33180", "08-9949-3294", null),
            ("1033530583", "บ้านอาลัย", "นางพิชาภัค บุดดี", new DateOnly(1940, 7, 15), null, "7", null, "สุขสวัสดิ์", "ไพรบึง", "33180", "0902624014", null),
            ("1033530584", "บ้านโพนปลัด", "นางประยงค์ ร่วมจิตร", new DateOnly(1939, 5, 8), null, "1", "พยุห์-ขุนหาญ", "สุขสวัสดิ์", "ไพรบึง", "33180", null, null),
            ("1033530521", "บ้านกู่", "นายอธิชาติ ตันทอง", new DateOnly(1924, 6, 15), null, "14", null, "กู่", "ปรางค์กู่", "33170", "045-6603338", "0839354451"),
            ("1033530526", "บ้านไฮน้อย", "นายพิจิตร์ ชวดรัมย์", new DateOnly(1956, 7, 12), null, "13", "ปรางค์กู่-สังขะ", "กู่", "ปรางค์กู่", "33170", null, null),
            ("1033530527", "บ้านกะดึ", "นางธนัญญา วุทธิยา", new DateOnly(1939, 7, 30), null, "16", null, "กู่", "ปรางค์กู่", "33170", "0981757837", null),
            ("1033530528", "บ้านพอก", "นางสาวินี พิมพ์จันทร์", new DateOnly(1939, 7, 1), null, "9", null, "กู่", "ปรางค์กู่", "33170", "0935451617", "0621933279"),
            ("1033530529", "บ้านเกาะกระโพธิ์", "ว่าที่ ร.ต.คมจักกฤช วุทธิยา", new DateOnly(1945, 9, 24), null, "11", null, "กู่", "ปรางค์กู่", "33170", "045-660330", null),
            ("1033530530", "บ้านหนองบัวตาคง", "นางนิตยา จำปาจีน", new DateOnly(1940, 1, 1), null, "4", null, "กู่", "ปรางค์กู่", "33170", "045660328", null),
            ("1033530531", "บ้านสามขา", "นางกนิษฐา ดวงสินธ์นิธิกุล", new DateOnly(1960, 11, 29), null, "5", null, "กู่", "ปรางค์กู่", "33170", "045660148", null),
            ("1033530532", "บ้านหว้า", "นายสาม รักษา", new DateOnly(1961, 5, 14), "33070180582", "2", null, "กู่", "ปรางค์กู่", "33170", null, null),
            ("1033530525", "บ้านขามหนองครอง", "นางอรุณรัตน์ แสนทวีสุข", new DateOnly(1974, 5, 30), null, "4", null, "ดู่", "ปรางค์กู่", "33170", "0990431428", "0990431428"),
            ("1033530522", "บ้านหนองคูอาวอย", "นางวรางคณา บุตรสอน", new DateOnly(1940, 6, 14), null, "8", null, "ดู่", "ปรางค์กู่", "33170", null, null),
            ("1033530523", "บ้านดู่", "นายนเรศ คำเสียง", new DateOnly(1947, 6, 24), null, "1", null, "ดู่", "ปรางค์กู่", "33170", "08-6153-9771", "08-6153-9771"),
            ("1033530524", "บ้านหนองแวง", "นายเกียรติพงษ์ พิศวงปราการ", new DateOnly(1956, 7, 11), null, "2", null, "ดู่", "ปรางค์กู่", "33170", "045660279", "0828467585"),
            ("1033530520", "บ้านตะเภา", "นางสาวรัชนี จันทร์แสง", new DateOnly(1974, 5, 16), null, "3", null, "ตูม", "ปรางค์กู่", "33170", "045660329", null),
            ("1033530514", "บ้านตูม", "นายสุนทร ประมวล", new DateOnly(1938, 7, 1), null, "1", null, "ตูม", "ปรางค์กู่", "33170", null, null),
            ("1033530515", "บ้านขี้นาค", "นายชำนาญ หงษ์สนิท", new DateOnly(1939, 7, 1), null, "6", null, "ตูม", "ปรางค์กู่", "33170", "045660332", null),
            ("1033530517", "บ้านบึงกระโพธิ์", "นายประหยัด ใจหวัง", new DateOnly(1939, 6, 12), null, "4", null, "ตูม", "ปรางค์กู่", "33170", null, "0872513999"),
            ("1033530506", "อนุบาลปรางค์กู่", "นางสร้อยทิพย์ ไทยน้อย", new DateOnly(1923, 5, 13), "33", "1", null, "พิมาย", "ปรางค์กู่", "33170", "045697076", "0818792897"),
            ("1033530507", "บ้านสนาย", "นายวัฒนา ทองมนต์", new DateOnly(1938, 7, 1), null, "8", "สนาย - สุโข", "พิมาย", "ปรางค์กู่", "33170", null, null),
            ("1033530508", "บ้านโนนดั่ง", "นางชณิกานต์ ศรีกำพล", new DateOnly(1957, 11, 28), null, "7", null, "พิมาย", "ปรางค์กู่", "33170", "045660177", null),
            ("1033530509", "บ้านขามฆ้อง", "นางมยุรี ศรีผา", new DateOnly(1964, 7, 10), null, "2", null, "พิมายเหนือ", "ปรางค์กู่", "33170", null, null),
            ("1033530510", "บ้านโป่ง", "นางสาวลักขณา จิตต์ภักดี", new DateOnly(1959, 7, 15), null, "6", null, "พิมายเหนือ", "ปรางค์กู่", "33170", "0927846362", null),
            ("1033530511", "บ้านไฮ", "นายบรรจบ สิงหร", new DateOnly(1939, 6, 21), null, "7", null, "พิมายเหนือ", "ปรางค์กู่", "33170", "0959842572", "0825399251"),
            ("1033530512", "บ้านโพธิ์สามัคคี", "นายทวีชัย วิเศษชาติ", new DateOnly(2006, 6, 12), null, "8", null, "พิมายเหนือ", "ปรางค์กู่", "33170", "045-969306", null),
            ("1033530513", "บ้านเหล็ก", "นางรัชฎาภรณ์ พิศวงปราการ", new DateOnly(1971, 5, 11), null, "4", "ปรางค์กู่", "พิมายเหนือ", "ปรางค์กู่", "33170", "045697290", null),
            ("1033530533", "บ้านนาวา", "นายพงษ์ศักดิ์ พงษ์สุวรรณ", new DateOnly(1928, 6, 27), null, "2", null, "โพธิ์ศรี", "ปรางค์กู่", "33170", "045660332", null),
            ("1033530534", "บ้านกอกหวาน", "นายศิโรตม์ เพ็งธรรม", new DateOnly(1937, 6, 5), null, "14", null, "โพธิ์ศรี", "ปรางค์กู่", "33170", "0981845380", null),
            ("1033530535", "บ้านบัลลังก์", "นายวีระชาติ ไชยชาญ", new DateOnly(1947, 6, 5), null, "6", null, "โพธิ์ศรี", "ปรางค์กู่", "33170", "0898444740", null),
            ("1033530536", "บ้านสมอ", "นายจำเนียร ใจนวน", new DateOnly(1924, 6, 15), null, "2", null, "สมอ", "ปรางค์กู่", "33170", "045660446", null),
            ("1033530537", "บ้านหนองเพดาน", "นางสาวมีนา ชูชื่น", new DateOnly(1938, 7, 1), null, "17", null, "สมอ", "ปรางค์กู่", "33170", "045660331", null),
            ("1033530538", "กระต่ายด่อนวิทยา", "นายวิทยา นาโสก", new DateOnly(1939, 6, 12), null, "13", null, "สมอ", "ปรางค์กู่", "33170", null, null),
            ("1033530539", "บ้านดอนหลี่", "นางสมยงค์ เชาวันกลาง", new DateOnly(1939, 5, 12), null, "4", null, "สมอ", "ปรางค์กู่", "33170", "0801745650", null),
            ("1033530540", "บ้านกุดปราสาท", "นายโชคชัย จุมพิศ", new DateOnly(1950, 11, 21), null, "3", null, "สมอ", "ปรางค์กู่", "33170", "045660336", null),
            ("1033530516", "บ้านสวายสนิท", "นายปัญญา พรหมศักดิ์", new DateOnly(1940, 7, 1), null, "1", null, "สวาย", "ปรางค์กู่", "33170", "045660325", "0810756251"),
            ("1033530518", "บ้านขามทับขอน", "นางสุชาดา คำเสียง", new DateOnly(1957, 10, 19), null, "3", null, "สวาย", "ปรางค์กู่", "33170", null, null),
            ("1033530519", "บ้านท่าคอยนาง", "นายบรรจง สระทอง", new DateOnly(1973, 5, 16), null, "5", null, "สวาย", "ปรางค์กู่", "33170", "0933839168", "0832754542"),
            ("1033530552", "บ้านสำโรงปราสาท", "นายปรัชรงค์ชัย เติมใจ", new DateOnly(1939, 5, 22), "3307-018956-3", "2", null, "สำโรงปราสาท", "ปรางค์กู่", "33170", null, null),
            ("1033530548", "บ้านตาเปียง", "นายประนุพงษ์ ระยับศรี", new DateOnly(1939, 6, 9), null, "1", null, "สำโรงปราสาท", "ปรางค์กู่", "33170", "045-960466", null),
            ("1033530549", "บ้านไฮเลิง", "นายวิฑูรย์ ไทยน้อย", new DateOnly(1951, 6, 15), null, "11", null, "สำโรงปราสาท", "ปรางค์กู่", "33170", "0901893349", "-0849585532"),
            ("1033530550", "บ้านหว้าน", "นายธนวรรธณ์ ทองดี", new DateOnly(1940, 6, 1), null, "8", "ปรางค์กู่-หนองห้าง-อุทมพร", "สำโรงปราสาท", "ปรางค์กู่", "33170", "0856572475", "045660333"),
            ("1033530551", "บ้านขอนแต้", "นายเอกชัย คุณมาศ", new DateOnly(1940, 8, 1), null, null, null, "สำโรงปราสาท", "ปรางค์กู่", "33170", null, null),
            ("1033530541", "บ้านหนองเชียงทูน", "นายสงวน ชุมวัน", new DateOnly(1927, 5, 7), null, "5", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", "0860846022", "0813897567"),
            ("1033530542", "บ้านกำแมด", "นางสุลัดดา ตอนศรี", new DateOnly(2002, 11, 29), null, "3", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", "045660335", null),
            ("1033530543", "บ้านศาลา", "นายกิตติกรณ์ ผ่านพินิจ", new DateOnly(1931, 11, 26), null, "2", "บ้านศาลา", "หนองเชียงทูน", "ปรางค์กู่", "33170", "045697500", "0982023524"),
            ("1033530544", "บ้านหนองระนาม", "นางสาวชัชฎา?ภรณ์? แหวนเงิน (รักษาการแทน)", new DateOnly(1946, 6, 28), null, "6", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", "0926491963", "0990722243"),
            ("1033530545", "บ้านมัดกานกทาขุมปูน", "นายสุจินต์ สมบัติวงศ์", new DateOnly(1970, 11, 13), null, "4 บ้านมัดกา", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", "045697283", null),
            ("1033530546", "บ้านหนองคูขาม", "นางสาวระเบียบ บุญชู", new DateOnly(1956, 7, 14), null, "7", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", null, "0860846022"),
            ("1033530547", "บ้านบ่อ", "นายวรรณนุพล จันทะสนธ์", new DateOnly(1957, 5, 26), null, "8", null, "หนองเชียงทูน", "ปรางค์กู่", "33170", "045620221", null),
            ("1033530764", "บ้านโคกตาล", "นางสาวอัจฉราภรณ์ ฉิมพินิจ", new DateOnly(1940, 10, 1), null, "1", null, "โคกตาล", "ภูสิงห์", "33140", null, null),
            ("1033530765", "บ้านลุมพุกคลองแก้ว", "นายบุญสม โสริโย", null, null, "2", null, "โคกตาล", "ภูสิงห์", "33140", null, null),
            ("1033530766", "บ้านศาลา", "นางณัฐธยาน์ ศรีสมนึก", new DateOnly(1958, 9, 1), null, "9", "ขุขันธ์ - โคกตาล", "โคกตาล", "ภูสิงห์", "33140", null, null),
            ("1033530767", "บ้านเรือทองคลองคำ", "นางศิริพรรณ มะนู", new DateOnly(1976, 9, 19), null, "5", null, "โคกตาล", "ภูสิงห์", "33140", null, null),
            ("1033530769", "บ้านนาตราว", "นายสมพงษ์ วงษ์ชาติ", new DateOnly(1944, 6, 11), null, "1", null, "ดงรัก", "ภูสิงห์", "33140", "045660132", null),
            ("1033530770", "บ้านจำปานวง", "นายสมหมาย ฉลาดรอบ", new DateOnly(1990, 6, 8), null, "3", null, "ดงรัก", "ภูสิงห์", "33140", null, null),
            ("1033530771", "บ้านแซรสะโบว", "นางวิชญฌาณ์ กรสันเทียะ", new DateOnly(1973, 7, 14), null, "6", null, "ดงรัก", "ภูสิงห์", "33140", null, null),
            ("1033530768", "บ้านตาโสม", "นางสาวธาริณี มั่นคง", new DateOnly(1961, 6, 12), null, "11", null, "ตะเคียนราม", "ภูสิงห์", "33140", null, null),
            ("1033530763", "บ้านตะเคียนราม", "นายชาญวิทย์ สันดอน", new DateOnly(1938, 7, 1), null, "2", null, "ตะเคียนราม", "ภูสิงห์", "33140", "045-969358", null),
            ("1033530772", "วนาสวรรค์", "นายมนตรี นันทวงศ์", new DateOnly(1982, 3, 10), null, "5", null, "ไพรพัฒนา", "ภูสิงห์", "33140", "045660128", "0819772506"),
            ("1033530773", "บ้านไพรพัฒนา", "นายพิชชวัฒน์ แสวงดี", new DateOnly(1973, 7, 14), "3303018901", "3", "ละลม - แซรไปร", "ไพรพัฒนา", "ภูสิงห์", "33140", "045920840", null),
            ("1033530774", "บ้านแซรไปร", "นายประเวช สูงสุด", new DateOnly(1964, 6, 9), null, "4", null, "ไพรพัฒนา", "ภูสิงห์", "33140", null, null),
            ("1033530775", "บ้านละลม", "นางสาวพัฒนา สังข์โกมล", new DateOnly(1938, 7, 1), null, "13", null, "ละลม", "ภูสิงห์", "33140", "045660169", null),
            ("1033530776", "บ้านธาตุพิทยาคม", "นางอรทัย สมยิ่ง", new DateOnly(1947, 6, 24), null, "3", null, "ละลม", "ภูสิงห์", "33140", "081-977-1180", "045660169"),
            ("1033530777", "บ้านขะยูง(โนนเจริญศึกษา)", "นางจิตนา มะโนเครื่อง", new DateOnly(1978, 9, 17), null, "4", null, "ละลม", "ภูสิงห์", "33140", "045-660170", "091-0209066"),
            ("1033530785", "บ้านพรหมเจริญ", "นายสุวิทย์ บุญวงค์", new DateOnly(1991, 5, 23), null, "5", "ละลม-ตาเม็ง", "ละลม", "ภูสิงห์", "33140", "0801640061", null),
            ("1033530781", "เพียงหลวง 14ฯ", "นางสาวปภาวรินทร์ ทองสุข", new DateOnly(1991, 8, 18), null, "3", null, "ห้วยตามอญ", "ภูสิงห์", "33140", "045660126", "0993275445"),
            ("1033530778", "บ้านทำนบ", "นายศักดิ์ ทรงศรี", new DateOnly(1956, 11, 21), null, "2", null, "ห้วยตามอญ", "ภูสิงห์", "33140", "045660168", "081-9773908"),
            ("1033530779", "บ้านห้วยตามอญ", "นายอดิศักดิ์ ธรรมพร", new DateOnly(1979, 4, 4), null, "6", null, "ห้วยตามอญ", "ภูสิงห์", "33140", "0615582882", null),
            ("1033530884", "บ้านพนมชัย", "นายไพฑูรย์ ผ่านพินิจ", new DateOnly(2002, 12, 18), null, "5", null, "ห้วยตามอญ", "ภูสิงห์", "33140", "045660125", null),
            ("1033530782", "อนุบาลภูสิงห์", "นางมลทา ทรงศรี", new DateOnly(1965, 5, 12), null, "1", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", null, null),
            ("1033530783", "บ้านโคกใหญ่", "นางสาวสุจิตรา สิทธิจันทร์", new DateOnly(1980, 3, 6), null, "14", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", null, null),
            ("1033530784", "บ้านตาเม็ง(อสพป.9)", "นางสาวอุทัย ศรีเลิศ", new DateOnly(1970, 5, 26), null, "8", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", "045660230", null),
            ("1033530786", "บ้านนกยูง(อสพป.30)", "นายสายชน แพงมา", new DateOnly(1956, 7, 25), null, "6", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", "045826521", null),
            ("1033530787", "บ้านทุ่งหลวง", "นางปทิตตา นันทวงศ์", new DateOnly(1991, 9, 19), null, "10", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", null, null),
            ("1033530780", "เคียงศิริบ้านโพธิ์ทอง", "นายศรีสุวรรณรัตน์ อาจอินทร์", new DateOnly(1975, 6, 10), null, "2", null, "ห้วยตึ๊กชู", "ภูสิงห์", "33140", null, null)
        };

        foreach (var s in schoolsData)
        {
            var key = s.Tambon + "_" + s.Amphoe;
            int? addressId = null;
            if (sdLookup.TryGetValue(key, out var sdId))
            {
                var addr = new Address { SubDistrictId = sdId, VillageName = s.Name };
                db.Addresses.Add(addr);
                await db.SaveChangesAsync();
                addressId = addr.Id;
            }

            db.Schools.Add(new School
            {
                SchoolCode = s.Code,
                NameTh = s.Name,
                Principal = s.Principal,
                EstablishedDate = s.EstablishedDate,
                TaxId = s.TaxId,
                SchoolLevel = s.Level,
                SchoolType = s.Type,
                Phone = s.Phone,
                Phone2 = s.Phone2,
                AreaId = area.Id,
                AreaTypeId = areaType.Id,
                AddressId = addressId,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] 196 schools from สพป.ศรีสะเกษ เขต 3 created.");

        // ── Seed PositionTypes ──
        if (!await db.PositionTypes.AnyAsync())
        {
            var positions = new[]
            {
                new PositionType { Code = "director",      NameTh = "ผู้อำนวยการสถานศึกษา",     Category = "ผู้บริหาร",    IsSchoolDirector = true,  SortOrder = 1 },
                new PositionType { Code = "acting_dir",    NameTh = "รักษาการผู้อำนวยการ",       Category = "ผู้บริหาร",    IsSchoolDirector = true,  SortOrder = 2 },
                new PositionType { Code = "deputy_dir",    NameTh = "รองผู้อำนวยการ",           Category = "ผู้บริหาร",    IsSchoolDirector = false, SortOrder = 3 },
                new PositionType { Code = "teacher_asst",  NameTh = "ครูผู้ช่วย",               Category = "ครู",         IsSchoolDirector = false, SortOrder = 10 },
                new PositionType { Code = "teacher_kc1",   NameTh = "ครู คศ.1",                 Category = "ครู",         IsSchoolDirector = false, SortOrder = 11 },
                new PositionType { Code = "teacher_kc2",   NameTh = "ครู คศ.2 ชำนาญการ",        Category = "ครู",         IsSchoolDirector = false, SortOrder = 12 },
                new PositionType { Code = "teacher_kc3",   NameTh = "ครู คศ.3 ชำนาญการพิเศษ",   Category = "ครู",         IsSchoolDirector = false, SortOrder = 13 },
                new PositionType { Code = "teacher_kc4",   NameTh = "ครู คศ.4 เชี่ยวชาญ",       Category = "ครู",         IsSchoolDirector = false, SortOrder = 14 },
                new PositionType { Code = "teacher_kc5",   NameTh = "ครู คศ.5 เชี่ยวชาญพิเศษ",  Category = "ครู",         IsSchoolDirector = false, SortOrder = 15 },
                new PositionType { Code = "clerk",         NameTh = "เจ้าหน้าที่ธุรการ",         Category = "เจ้าหน้าที่", IsSchoolDirector = false, SortOrder = 20 },
                new PositionType { Code = "librarian",     NameTh = "บรรณารักษ์",               Category = "เจ้าหน้าที่", IsSchoolDirector = false, SortOrder = 21 },
                new PositionType { Code = "janitor",       NameTh = "นักการภารโรง",             Category = "เจ้าหน้าที่", IsSchoolDirector = false, SortOrder = 22 },
                new PositionType { Code = "security",      NameTh = "ยามรักษาความปลอดภัย",      Category = "เจ้าหน้าที่", IsSchoolDirector = false, SortOrder = 23 },
                new PositionType { Code = "area_officer",  NameTh = "เจ้าหน้าที่เขตพื้นที่",     Category = "เจ้าหน้าที่เขต", IsSchoolDirector = false, SortOrder = 30 },
                new PositionType { Code = "area_specialist", NameTh = "ศึกษานิเทศก์",           Category = "เจ้าหน้าที่เขต", IsSchoolDirector = false, SortOrder = 31 },
            };
            db.PositionTypes.AddRange(positions);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] {positions.Length} position types created.");
        }

        // ── Seed AcademicStandingTypes (วิทยฐานะ) ──
        if (!await db.AcademicStandingTypes.AnyAsync())
        {
            var standings = new[]
            {
                new AcademicStandingType { Code = "none",          NameTh = "ยังไม่มีวิทยฐานะ",  Level = 0 },
                new AcademicStandingType { Code = "proficient",    NameTh = "ชำนาญการ",         Level = 1 },
                new AcademicStandingType { Code = "senior",        NameTh = "ชำนาญการพิเศษ",    Level = 2 },
                new AcademicStandingType { Code = "expert",        NameTh = "เชี่ยวชาญ",        Level = 3 },
                new AcademicStandingType { Code = "senior_expert", NameTh = "เชี่ยวชาญพิเศษ",   Level = 4 },
            };
            db.AcademicStandingTypes.AddRange(standings);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] {standings.Length} academic standing types created.");
        }

        // ── Seed EducationLevels (ระดับวุฒิการศึกษา) ──
        if (!await db.EducationLevels.AnyAsync())
        {
            var levels = new[]
            {
                new EducationLevel { Code = "p6",    NameTh = "ประถมศึกษา",                       NameEn = "Primary",                     Level = 1  },
                new EducationLevel { Code = "m3",    NameTh = "มัธยมศึกษาตอนต้น",                  NameEn = "Lower Secondary",             Level = 2  },
                new EducationLevel { Code = "m6",    NameTh = "มัธยมศึกษาตอนปลาย",                 NameEn = "Upper Secondary",             Level = 3  },
                new EducationLevel { Code = "pvc",   NameTh = "ประกาศนียบัตรวิชาชีพ (ปวช.)",       NameEn = "Vocational Certificate",      Level = 4  },
                new EducationLevel { Code = "pvc2",  NameTh = "ประกาศนียบัตรวิชาชีพชั้นสูง (ปวส.)", NameEn = "High Vocational Certificate", Level = 5  },
                new EducationLevel { Code = "ba",    NameTh = "ปริญญาตรี",                         NameEn = "Bachelor's Degree",           Level = 6  },
                new EducationLevel { Code = "ma",    NameTh = "ปริญญาโท",                          NameEn = "Master's Degree",             Level = 7  },
                new EducationLevel { Code = "phd",   NameTh = "ปริญญาเอก",                         NameEn = "Doctoral Degree",             Level = 8  },
                new EducationLevel { Code = "other", NameTh = "อื่นๆ",                             NameEn = "Other",                       Level = 0  },
            };
            db.EducationLevels.AddRange(levels);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] {levels.Length} education levels created.");
        }

        // ── Seed default work groups for area ──
        if (!await db.WorkGroups.AnyAsync(w => w.ScopeType == "Area" && w.ScopeId == area.Id))
        {
            var areaWorkGroups = new[]
            {
                new WorkGroup { Name = "กลุ่มอำนวยการ",              ScopeType = "Area", ScopeId = area.Id, SortOrder = 1, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มนโยบายและแผน",          ScopeType = "Area", ScopeId = area.Id, SortOrder = 2, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มบริหารงานบุคคล",         ScopeType = "Area", ScopeId = area.Id, SortOrder = 3, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มบริหารงานการเงินฯ",      ScopeType = "Area", ScopeId = area.Id, SortOrder = 4, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มนิเทศติดตามฯ",          ScopeType = "Area", ScopeId = area.Id, SortOrder = 5, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มส่งเสริมการจัดการศึกษา",  ScopeType = "Area", ScopeId = area.Id, SortOrder = 6, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่ม ICT",                  ScopeType = "Area", ScopeId = area.Id, SortOrder = 7, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "หน่วยตรวจสอบภายใน",          ScopeType = "Area", ScopeId = area.Id, SortOrder = 8, CreatedAt = DateTimeOffset.UtcNow },
                new WorkGroup { Name = "กลุ่มกฎหมายและคดี",           ScopeType = "Area", ScopeId = area.Id, SortOrder = 9, CreatedAt = DateTimeOffset.UtcNow },
            };
            db.WorkGroups.AddRange(areaWorkGroups);
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] {areaWorkGroups.Length} area work groups created.");
        }
    }
}
