using System;
using System.Numerics;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Numeric
{
    /// <summary>Exact intermediate arithmetic. DTOs persist numerator/denominator as decimal strings.</summary>
    public readonly struct Rational : IComparable<Rational>
    {
        public readonly BigInteger Numerator;
        private readonly BigInteger denominator;
        public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
        public Rational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
            Numerator = numerator / gcd; this.denominator = denominator / gcd;
        }
        public static implicit operator Rational(long value) => new Rational(value, 1);
        public static Rational operator +(Rational a, Rational b) => new Rational(a.Numerator*b.Denominator+b.Numerator*a.Denominator,a.Denominator*b.Denominator);
        public static Rational operator -(Rational a, Rational b) => new Rational(a.Numerator*b.Denominator-b.Numerator*a.Denominator,a.Denominator*b.Denominator);
        public static Rational operator *(Rational a, Rational b) => new Rational(a.Numerator*b.Numerator,a.Denominator*b.Denominator);
        public static Rational operator /(Rational a, Rational b) => new Rational(a.Numerator*b.Denominator,a.Denominator*b.Numerator);
        public int CompareTo(Rational other) => (Numerator*other.Denominator).CompareTo(other.Numerator*Denominator);
        public static bool operator <(Rational a,Rational b) => a.CompareTo(b)<0;
        public static bool operator >(Rational a,Rational b) => a.CompareTo(b)>0;
        public static bool operator <=(Rational a,Rational b) => a.CompareTo(b)<=0;
        public static bool operator >=(Rational a,Rational b) => a.CompareTo(b)>=0;
        public static Rational Clamp(Rational x,Rational min,Rational max) => x<min?min:x>max?max:x;
        public long RoundNonNegative()
        {
            if(Numerator.Sign<0) throw new ArgumentOutOfRangeException(nameof(Numerator));
            return checked((long)((2*Numerator+Denominator)/(2*Denominator)));
        }
        public double ToDouble() => (double)Numerator/(double)Denominator;
        public override string ToString() => Numerator+"/"+Denominator;
    }

    public static class NumericKernel
    {
        public const long Micro = 1000000;
        public const string Version = "NumericRationalV1";
        public const string SqrtVersion = "SqrtRational12V1";
        public static Rational FromMicro(long value) => new Rational(value,Micro);
        public static long ToMicro(Rational value) => (value*Micro).RoundNonNegative();
        public static BigInteger IntegerSqrt(BigInteger value)
        {
            if(value.Sign<0) throw new ArgumentOutOfRangeException(nameof(value));
            if(value<2) return value;
            var x=value; var y=(x+1)/2;
            while(y<x) { x=y; y=(x+value/x)/2; }
            return x;
        }
        public static BigInteger SqrtRational12(Rational value)
        {
            if(value.Numerator.Sign<0) throw new ArgumentOutOfRangeException(nameof(value));
            var a=value.Numerator*BigInteger.Pow(10,24); var b=value.Denominator;
            var k=IntegerSqrt(a/b);
            return 4*a>=b*(2*k+1)*(2*k+1)?k+1:k;
        }
        public static bool InjuryOccurs(Rational fatigue,int reductionBasisPoints,uint roll)
        {
            if(reductionBasisPoints<0 || reductionBasisPoints>10000) throw new ArgumentOutOfRangeException(nameof(reductionBasisPoints));
            var definition=TrainingContent.Current.Fatigue;
            var safe=FromMicro(definition.SafeMicro);var mandatory=FromMicro(definition.MandatoryInjuryMicro);
            if(fatigue<=safe) return false;
            if(fatigue>=mandatory) return true;
            var u=new Rational(roll,BigInteger.One<<32)+new Rational(reductionBasisPoints,10000);
            return u*u<(fatigue-safe)/(mandatory-safe);
        }
    }
}
