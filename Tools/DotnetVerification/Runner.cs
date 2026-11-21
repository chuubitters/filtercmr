using System; using System.Linq; using System.Reflection; using NUnit.Framework;
public static class Runner {
  public static int RunAll(Assembly asm, string filter=null){
    int pass=0, fail=0;
    foreach(var t in asm.GetTypes().Where(t=>t.GetCustomAttribute<TestFixtureAttribute>()!=null).OrderBy(t=>t.Name))
      foreach(var m in t.GetMethods().OrderBy(m=>m.MetadataToken)){
        var cases = m.GetCustomAttributes<TestCaseAttribute>().Select(c=>c.Args).ToList();
        if(m.GetCustomAttribute<TestAttribute>()!=null) cases.Add(new object[0]);
        foreach(var args in cases){
          string name=$"{t.Name}.{m.Name}({string.Join(",",args.Select(a=>a is string s? (s.Length>30?s.Substring(0,30)+"…":s):a))})";
          if(filter!=null && !name.Contains(filter)) continue;
          var inst=Activator.CreateInstance(t);
          try{ foreach(var su in t.GetMethods().Where(x=>x.GetCustomAttribute<SetUpAttribute>()!=null)) su.Invoke(inst,null);
               m.Invoke(inst,args); pass++; }
          catch(TargetInvocationException e){ fail++; Console.WriteLine($"FAIL {name}: {e.InnerException.GetType().Name}: {e.InnerException.Message}"); }
        }
      }
    Console.WriteLine($"{pass} passed, {fail} failed");
    return fail;
  }
}
