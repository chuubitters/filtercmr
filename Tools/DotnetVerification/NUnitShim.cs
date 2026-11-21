// Minimal NUnit-compatible shim so Unity EditMode test sources run under plain dotnet.
using System;
namespace NUnit.Framework {
  [AttributeUsage(AttributeTargets.Class)] public class TestFixtureAttribute : Attribute {}
  [AttributeUsage(AttributeTargets.Method)] public class TestAttribute : Attribute {}
  [AttributeUsage(AttributeTargets.Method, AllowMultiple=true)] public class TestCaseAttribute : Attribute { public object[] Args; public TestCaseAttribute(params object[] a){Args=a;} }
  [AttributeUsage(AttributeTargets.Method)] public class SetUpAttribute : Attribute {}
  public class AssertionException : Exception { public AssertionException(string m):base(m){} }
  public static class Assert {
    static void F(string m){ throw new AssertionException(m); }
    public static void Fail(string m=null)=>F(m??"Fail");
    public static void IsTrue(bool c,string m=null){ if(!c) F(m??"expected true"); }
    public static void IsFalse(bool c,string m=null){ if(c) F(m??"expected false"); }
    public static void IsNull(object o,string m=null){ if(o!=null) F(m??"expected null"); }
    public static void IsNotNull(object o,string m=null){ if(o==null) F(m??"expected not null"); }
    public static void AreEqual(object e,object a,string m=null){ if(!Equals(e,a)) F($"{m} expected <{e}> but was <{a}>"); }
    public static void AreEqual(double e,double a,double d,string m=null){ if(Math.Abs(e-a)>d) F($"{m} expected <{e}> ±{d} but was <{a}>"); }
    public static void AreNotEqual(object e,object a,string m=null){ if(Equals(e,a)) F($"{m} expected not <{e}>"); }
    public static void Greater(double a,double b,string m=null){ if(!(a>b)) F($"{m} expected {a} > {b}"); }
    public static void Less(double a,double b,string m=null){ if(!(a<b)) F($"{m} expected {a} < {b}"); }
    public static void GreaterOrEqual(double a,double b,string m=null){ if(!(a>=b)) F($"{m} expected {a} >= {b}"); }
    public static void LessOrEqual(double a,double b,string m=null){ if(!(a<=b)) F($"{m} expected {a} <= {b}"); }
  }
}
