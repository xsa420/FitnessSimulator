using System;
using System.Linq;
using System.Numerics;
using System.Globalization;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;
namespace FitnessSimulator.Game.Life.Relationships
{
    [Serializable] public sealed class SocialCheckSnapshot
    {
        public string InteractionId, ActorId, ContentVersion, ProbabilityNumerator, ProbabilityDenominator;
        public long BodyRevision, EffectiveCharmMicro, MentalStressMicro, BaseProbabilityMillionths;
    }
    public sealed class SocialCheckResult { public SocialCheckSnapshot Snapshot; public uint Sample; public bool Success; }
    public static class SocialRules
    {
        // Caller owns event identity/eligibility, the private random stream and the atomic chosen outcome effects.
        public static SocialCheckSnapshot FreezeCheck(string interactionId,BodyState body,long baseProbabilityMillionths)
        {
            if(string.IsNullOrWhiteSpace(interactionId)||baseProbabilityMillionths<0||baseProbabilityMillionths>1000000)throw new ArgumentException("Invalid social check definition");
            body.Validate();var rule=RelationshipContent.Definition.Social;
            long effective=BodyMetrics.GetEffectiveCharmMicro(body);
            var x=Normalize(effective,"base_charm");var y=Normalize(body.MentalStressMicro,"mental_stress");
            var sqrt=new Rational(NumericKernel.SqrtRational12(x),BigInteger.Pow(10,12));
            var probability=Rational.Clamp(new Rational(baseProbabilityMillionths,1000000)*(1+new Rational(rule.CharmSqrtMillionths,1000000)*sqrt)*(1-new Rational(rule.MentalSquareMillionths,1000000)*y*y),0,1);
            return new SocialCheckSnapshot { InteractionId=interactionId,ActorId=body.ActorId,BodyRevision=body.Revision,ContentVersion=RelationshipContent.Definition.Version,
                EffectiveCharmMicro=effective,MentalStressMicro=body.MentalStressMicro,BaseProbabilityMillionths=baseProbabilityMillionths,
                ProbabilityNumerator=probability.Numerator.ToString(CultureInfo.InvariantCulture),ProbabilityDenominator=probability.Denominator.ToString(CultureInfo.InvariantCulture) };
        }
        public static SocialCheckResult Resolve(SocialCheckSnapshot snapshot,uint sample)
        {
            if(snapshot==null||string.IsNullOrWhiteSpace(snapshot.ActorId)||string.IsNullOrWhiteSpace(snapshot.InteractionId)||string.IsNullOrWhiteSpace(snapshot.ContentVersion)||snapshot.BodyRevision<0)throw new ArgumentException("Invalid frozen social snapshot");
            var p=new Rational(BigInteger.Parse(snapshot.ProbabilityNumerator,CultureInfo.InvariantCulture),BigInteger.Parse(snapshot.ProbabilityDenominator,CultureInfo.InvariantCulture));
            if(p<0||p>1)throw new ArgumentException("Invalid frozen social probability");
            return new SocialCheckResult { Snapshot=snapshot,Sample=sample,Success=new Rational(sample,4294967296L)<p };
        }
        // Units are supplied by the registered effect (integer money, or micro K/X), never field reflection.
        public static long ScalePositiveEffect(BodyState body,string effectKind,long amount,bool isPositiveScalable)
        {
            if(!isPositiveScalable||amount<=0)return amount;
            var curve=RelationshipContent.Definition.Social;
            if(!curve.ScalableEffectKinds.Contains(effectKind))throw new ArgumentException("Social scaling forbids costs/items/tier rewards/fixed affection: "+effectKind);
            body.Validate();var x=Normalize(BodyMetrics.GetEffectiveCharmMicro(body),"base_charm");
            return ((Rational)amount*(1+new Rational(curve.PositiveLinearMillionths,1000000)*x)).RoundNonNegative();
        }
        private static Rational Normalize(long micro,string attributeId)
        {
            var range=BodyContent.Attribute(attributeId);
            if(range.MaxMicro<=range.MinMicro)throw new ArgumentException("Invalid social normalization range: "+attributeId);
            return Rational.Clamp(new Rational(micro-range.MinMicro,range.MaxMicro-range.MinMicro),0,1);
        }
    }
}