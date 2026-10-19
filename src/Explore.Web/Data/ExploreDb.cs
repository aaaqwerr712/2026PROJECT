using Explore.Shared;
using Microsoft.EntityFrameworkCore;
namespace Explore.Web.Data;
public class ExploreDb(DbContextOptions<ExploreDb> options) : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();
}
public static class Seed
{
    public static readonly Trail[] Trails = [
        new(1, "老城慢行：找回日常的風景", "從街屋、工藝到河岸，用一個下午走進地方故事。", 150, [1, 2, 3]),
        new(2, "山海之間：給自己一點空白", "穿過綠蔭，走向海風，收集沿途的光與聲音。", 180, [4, 5, 6])
    ];
    public static Place[] Places => [
        Make(1,"時光街屋","文化走讀","沿著紅磚與木窗，讀一段老街的生活記憶。","oldtown",40),
        Make(2,"巷口工藝所","地方工藝","走進小小的工作室，看見手作留下的溫度。","craft",45),
        Make(3,"河岸散步道","自然風景","讓腳步慢下來，聽風穿過河畔的樹梢。","river",35),
        Make(4,"綠蔭森林步道","自然風景","循著樹影前行，把城市的喧囂留在身後。","forest",60),
        Make(5,"海風觀景台","自然風景","站在山海交會的地方，等待一場溫柔的日落。","coast",30),
        Make(6,"山間茶屋","地方工藝","用一杯茶的時間，認識土地與人的連結。","tea",50)
    ];
    private static Place Make(int id, string name, string category, string summary, string theme, int minutes) => new() {
        Name=name, Category=category, Summary=summary, Theme=theme, VisitMinutes=minutes,
        Description=summary+" 這是虛構的示範據點，用來展示景點介紹、路線串接與 API 資料流程。正式發布活動前，請替換成經確認的場域資料、開放時間及交通資訊。",
        Address="示範場域・非實際營業地址", Latitude=23.7+id*0.01, Longitude=120.9+id*0.01
    };
}
