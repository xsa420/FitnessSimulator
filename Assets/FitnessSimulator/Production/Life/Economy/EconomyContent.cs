using System;
using System.Linq;
using System.Globalization;
using FitnessSimulator.Game.Configuration;
namespace FitnessSimulator.Game.Life.Economy
{
    [Serializable] public sealed class EconomyContentDocument
    {
        public string Version, StateVersion;
        public InventoryInitialDefinition InitialInventory;
        public ItemContentDefinition[] Items;
        public NotebookEffectDefinition[] Effects;
        public ShopContentDefinition[] Shops;
        public ItemPointDefinition[] OperationPoints;
        public string DefaultOperationPointId;
    }
    [Serializable] public sealed class InventoryInitialDefinition { public string Id; public ItemStack[] Items; }
    [Serializable] public sealed class ItemContentDefinition { public string Id, Name, EffectId; public ItemKind Kind; }
    [Serializable] public sealed class ItemPointDefinition { public string Id; public string[] AllowedItemIds; }
    [Serializable] public sealed class ShopContentDefinition
    {
        public string Id, Name;
        public int RefreshPeriod, OfferSlots;
        public string[] InitiallyUnlockedProductIds;
        public ShopProductContent[] Products;
    }
    [Serializable] public sealed class ShopProductContent { public ProductDefinition Product; public ConditionDefinition Unlock; }
    public static class EconomyContent
    {
        public const string Notebook="training_notebook", Bookmark="commemorative_bookmark", Gym="shop_gym", Corner="shop_corner";
        public const string EmptyInventoryId="LC.empty-notebook0-bookmark0.v1";
        public static EconomyContentDocument Definition => ContentRuntime.Current.Get<EconomyContentDocument>("economy");
        public static string Version => Definition.StateVersion;
        public static string[] AllShopIds => Definition.Shops.Select(x=>x.Id).ToArray();
        public static string[] AllItemIds => Definition.Items.Select(x=>x.Id).ToArray();
        public static ShopContentDefinition Shop(string id) => Definition.Shops.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("economy.Shops: unknown "+id);
        public static ItemContentDefinition Item(string id) => Definition.Items.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("economy.Items: unknown "+id);
        public static ItemKind Kind(string id)=>Item(id).Kind;
        public static int RefreshPeriod(string id)=>Shop(id).RefreshPeriod;
        public static ProductDefinition[] Products(string id)=>Shop(id).Products.Select(x=>x.Product).ToArray();
        public static ProductDefinition Product(string shop,string id)=>Products(shop).SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("economy.Shops."+shop+": unknown product "+id);
        public static NotebookEffectDefinition ItemEffect(string itemId)
        {
            string effect=Item(itemId).EffectId;
            return Definition.Effects.SingleOrDefault(x=>x.Id==effect)??throw new ArgumentException("economy.Items."+itemId+": missing fixed effect");
        }
        public static NotebookEffectDefinition NotebookEffect()=>ItemEffect(Notebook);
        public static string[] AllowedItems(string definitionId)=>Definition.OperationPoints.SingleOrDefault(x=>x.Id==definitionId)?.AllowedItemIds??throw new ArgumentException("economy.OperationPoints: unknown "+definitionId);
        public static bool ProductUnlocked(string shopId,string productId,int day,int veteran,int beginner)
        {
            var condition=Shop(shopId).Products.Single(x=>x.Product.Id==productId).Unlock;
            return ConditionEvaluator.Evaluate(condition,key=> (key=="day"?day:key=="veteranAffection"?veteran:beginner).ToString(CultureInfo.InvariantCulture),AllowedUnlockFact);
        }
        private static bool AllowedUnlockFact(string key)=>key=="day"||key=="veteranAffection"||key=="beginnerAffection";
        public static void ValidateContent()
        {
            var d=Definition;
            if(string.IsNullOrWhiteSpace(d.Version)||string.IsNullOrWhiteSpace(d.StateVersion)||d.Items==null||d.Items.Length<2||d.Items.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.Name)||!Enum.IsDefined(typeof(ItemKind),x.Kind))||d.Items.Select(x=>x.Id).Distinct().Count()!=d.Items.Length)
                throw new ArgumentException("economy.Items: invalid definitions");
            if(d.Effects==null||d.Effects.Any(e=>e==null||string.IsNullOrWhiteSpace(e.Id)||e.ConfigVersion!=d.StateVersion||e.Kind!="AllExerciseKnowledge"||e.KnowledgeMicroPerExercise<=0||e.KnowledgeMicroPerExercise>100000000||e.ExerciseIds==null||e.ExerciseIds.Length==0||e.ExerciseIds.Distinct().Count()!=e.ExerciseIds.Length)||d.Effects.Select(e=>e.Id).Distinct().Count()!=d.Effects.Length)
                throw new ArgumentException("economy.Effects: invalid fixed effect");
            foreach(var item in d.Items)if(item.Kind==ItemKind.ActiveUse?!d.Effects.Any(e=>e.Id==item.EffectId):item.EffectId!="")throw new ArgumentException("economy.Items."+item.Id+": invalid effect reference");
            foreach(var effect in d.Effects)foreach(string id in effect.ExerciseIds)FitnessSimulator.Game.Training.ExerciseCatalog.Get(id);
            var initial=d.InitialInventory;
            if(initial==null||initial.Id!=EmptyInventoryId||initial.Items==null||initial.Items.Length!=d.Items.Length||initial.Items.Any(x=>x==null||x.Quantity!=0||!d.Items.Any(i=>i.Id==x.ItemId))||initial.Items.Select(x=>x.ItemId).Distinct().Count()!=initial.Items.Length)
                throw new ArgumentException("economy.InitialInventory: explicit complete empty inventory required");
            if(d.Shops==null||d.Shops.Length==0||d.Shops.Any(x=>x==null)||d.Shops.Select(x=>x.Id).Distinct().Count()!=d.Shops.Length)throw new ArgumentException("economy.Shops: invalid ids");
            foreach(var shop in d.Shops)
            {
                if(string.IsNullOrWhiteSpace(shop.Id)||string.IsNullOrWhiteSpace(shop.Name)||shop.RefreshPeriod<1||shop.OfferSlots<1||shop.Products==null||shop.Products.Length==0||shop.Products.Any(p=>p==null||p.Product==null)||shop.Products.Select(p=>p.Product.Id).Distinct().Count()!=shop.Products.Length||shop.InitiallyUnlockedProductIds==null||shop.InitiallyUnlockedProductIds.Length==0||shop.InitiallyUnlockedProductIds.Distinct().Count()!=shop.InitiallyUnlockedProductIds.Length||shop.InitiallyUnlockedProductIds.Any(id=>!shop.Products.Any(p=>p.Product.Id==id)))throw new ArgumentException("economy.Shops."+shop.Id+": invalid pool/initial/slots");
                foreach(var product in shop.Products)
                {
                    var p=product.Product;
                    if(string.IsNullOrWhiteSpace(p.Id)||p.ShopId!=shop.Id||!d.Items.Any(i=>i.Id==p.ItemId)||p.UnitsPerPackage<=0||p.Price<0||p.PackagesPerRefresh<=0)throw new ArgumentException("economy.Shops."+shop.Id+"."+p.Id+": invalid package");
                    ConditionEvaluator.Validate(product.Unlock,AllowedUnlockFact,"economy.Shops."+shop.Id+"."+p.Id+".Unlock");
                }
            }
            if(d.OperationPoints==null||d.OperationPoints.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Id)||p.AllowedItemIds==null||p.AllowedItemIds.Length==0||p.AllowedItemIds.Distinct().Count()!=p.AllowedItemIds.Length||p.AllowedItemIds.Any(id=>!d.Items.Any(i=>i.Id==id&&i.Kind==ItemKind.ActiveUse)))||d.OperationPoints.Select(p=>p.Id).Distinct().Count()!=d.OperationPoints.Length||!d.OperationPoints.Any(p=>p.Id==d.DefaultOperationPointId))throw new ArgumentException("economy.OperationPoints: invalid allowed items/default");
        }
    }
}