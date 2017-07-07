using LINQPad;
using Microsoft.CSharp;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Rophuine.LINQPad.ObjectExtend
{
    public static class ObjectExtensions
    {
        public static dynamic Extend(this object firstObj, params object[] objs)
        {
            var result = new ExpandoObject();
            foreach (var o in new[] { firstObj }.Union(objs))
            {
                foreach (var p in o.GetType().GetProperties().Select(p => new { Name = p.Name, Value = p.GetValue(o) }))
                {
                    var props = ((IDictionary<string, object>)result);
                    if (props.ContainsKey(p.Name)) props[p.Name] = p.Value;
                    else props.Add(p.Name, p.Value);
                }
            }
            return ConvertExpandoToAnonymous(result);
        }

        static object ConvertExpandoToAnonymous(ExpandoObject eo)
        {
            var d = eo as IDictionary<string, object>;
            var typeMap = d.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetType());

            var type = CreateType(typeMap);
            var result = Activator.CreateInstance(type);
            foreach (var member in d)
            {
                type.GetProperty(member.Key).SetValue(result, member.Value);
            }
            return result;
        }

        static ConcurrentDictionary<string, Type> _anonymousTypes = new ConcurrentDictionary<string, System.Type>();
        static Type CreateType(IDictionary<string, Type> fields)
        {
            return _anonymousTypes.GetOrAdd(GetTypeHash(fields), h => CreateTypeUncached(fields));
        }
        static Type CreateTypeUncached(IDictionary<string, Type> fields)
        {
            var typename = "anonymous_" + Guid.NewGuid().ToString("N");

            var compileUnit = new CodeCompileUnit();
            var ns = new CodeNamespace("MakeLinqpadBetter.Dynamic");
            compileUnit.Namespaces.Add(ns);
            ns.Imports.Add(new CodeNamespaceImport("System"));

            var classType = new CodeTypeDeclaration(typename);
            classType.Attributes = MemberAttributes.Public;
            ns.Types.Add(classType);

            foreach (var prop in fields)
            {
                var fieldName = "_" + prop.Key;
                var field = new CodeMemberField(prop.Value, fieldName);
                classType.Members.Add(field);

                var property = new CodeMemberProperty();
                property.Attributes = MemberAttributes.Public | MemberAttributes.Final;
                property.Type = new CodeTypeReference(prop.Value);
                property.Name = prop.Key;
                property.GetStatements.Add(new CodeMethodReturnStatement(new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), fieldName)));
                property.SetStatements.Add(new CodeAssignStatement(new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), fieldName), new CodePropertySetValueReferenceExpression()));
                classType.Members.Add(property);
            }

            var extend = new CodeMemberMethod() { Name = "Extend", ReturnType = new CodeTypeReference(typeof(object)), Attributes = MemberAttributes.Public };
            extend.Parameters.Add(new CodeParameterDeclarationExpression("params object[]", "objs"));
            extend.Statements.Add(new CodeMethodReturnStatement(new CodeMethodInvokeExpression(new CodeTypeReferenceExpression(typeof(ObjectExtensions).FullName), "Extend", new CodeThisReferenceExpression(), new CodeArgumentReferenceExpression("objs"))));
            classType.Members.Add(extend);

            var dump = new CodeMemberMethod() { Name = "Dump", ReturnType = new CodeTypeReference(typeof(object)), Attributes = MemberAttributes.Public };
            dump.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(string)), "description"));
            dump.Statements.Add(new CodeMethodReturnStatement(new CodeMethodInvokeExpression(new CodeTypeReferenceExpression("LINQPad.Extensions"), "Dump", new CodeThisReferenceExpression(), new CodeArgumentReferenceExpression("description"))));
            classType.Members.Add(dump);

            var dumpNoParams = new CodeMemberMethod() { Name = "Dump", ReturnType = new CodeTypeReference(typeof(object)), Attributes = MemberAttributes.Public };
            dumpNoParams.Statements.Add(new CodeMethodReturnStatement(new CodeMethodInvokeExpression(new CodeTypeReferenceExpression("LINQPad.Extensions"), "Dump", new CodeThisReferenceExpression())));
            classType.Members.Add(dumpNoParams);

            var results = _cSharpCodeProvider.Value.CompileAssemblyFromDom(_compilerParameters.Value, compileUnit);
            if (results.Errors.Count > 0)
            {
                results.Dump();
                throw new Exception("Unable to build anonymous type");
            }
            return results.CompiledAssembly.GetTypes().Single();
        }

        static Lazy<CSharpCodeProvider> _cSharpCodeProvider = new Lazy<Microsoft.CSharp.CSharpCodeProvider>(GetCSharpProvider, true);
        static CSharpCodeProvider GetCSharpProvider()
        {
            var csc = new CSharpCodeProvider(new Dictionary<string, string>() { { "CompilerVersion", "v4.0" } });
            return csc;
        }

        static Lazy<CompilerParameters> _compilerParameters = new Lazy<System.CodeDom.Compiler.CompilerParameters>(GetCompilerParameters, true);
        static CompilerParameters GetCompilerParameters()
        {
            var parameters = new CompilerParameters(new[] { "mscorlib.dll", "System.Core.dll" });
            parameters.GenerateInMemory = true;
            parameters.GenerateExecutable = false;
            parameters.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            parameters.ReferencedAssemblies.Add(Assembly.Load("LINQPad").Location);
            return parameters;
        }

        static string GetTypeHash(IDictionary<string, Type> fields)
        {
            var s = string.Join("|", fields.Select(kvp => $"{kvp.Key}/{kvp.Value}").OrderBy(k => k));
            return Encoding.ASCII.GetString(MD5.Create().ComputeHash(Encoding.ASCII.GetBytes(s)));
        }
    }
}
