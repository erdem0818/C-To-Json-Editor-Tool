using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    namespace Core.Editor
    {
        public class CSharpToJsonConverter : EditorWindow
        {
            private string _csharpText = "";
            private string _jsonText = "";
            private Vector2 _csharpScrollPos;
            private Vector2 _jsonScrollPos;
            private bool _serializeAllSeparately;
            private bool _tryInitialize;

            [MenuItem("Tools/C# to Json Converter")]
            public static void ShowWindow()
            {
                GetWindow<CSharpToJsonConverter>("C# to JSON Converter");
            }

            private void OnGUI()
            {
                GUILayout.Label("C# to JSON Converter", EditorStyles.boldLabel);
                _serializeAllSeparately = GUILayout.Toggle(_serializeAllSeparately, "Serialize Separately");
                _tryInitialize          = GUILayout.Toggle(_tryInitialize, "Try Initialize");
                CSharpScriptExecutorRoslyn.TryInitialize = _tryInitialize;
                
                GUILayout.Label("C# Code:", EditorStyles.boldLabel);
                _csharpScrollPos = EditorGUILayout.BeginScrollView(_csharpScrollPos, GUILayout.Height(200));
                _csharpText      = EditorGUILayout.TextArea(_csharpText, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                GUILayout.Label("JSON:", EditorStyles.boldLabel);
                _jsonScrollPos = EditorGUILayout.BeginScrollView(_jsonScrollPos, GUILayout.Height(200));
                _jsonText      = EditorGUILayout.TextArea(_jsonText, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Convert"))
                {
                    ConvertCSharpToJson();
                }

                if (GUILayout.Button("Beautify"))
                {
                    BeautifyJson();
                }

                GUILayout.EndHorizontal();
            }

            private void ConvertCSharpToJson()
            {
                try
                {
                    _jsonText = string.Empty;
                    
                    var settings = new JsonSerializerSettings
                    {
                        Converters = new JsonConverter[] { new Vector3Converter() , new QuaternionConverter() },
                        Formatting = Formatting.Indented
                    };
                    
                    if (!_serializeAllSeparately)
                    {
                        var instance = CSharpScriptExecutorRoslyn.CreateInstance(_csharpText);
                        _jsonText = JsonConvert.SerializeObject(instance, settings);
                    }
                    else
                    {
                        var instances = CSharpScriptExecutorRoslyn.CreateInstances(_csharpText);
                        StringBuilder builder = new StringBuilder();
                        foreach (var obj in instances)
                        {
                            var serialized = JsonConvert.SerializeObject(obj, settings);
                            builder.Append($"[{obj.GetType().Name}]");
                            builder.AppendLine();
                            builder.Append(serialized);
                            builder.AppendLine();
                        }
                        _jsonText = builder.ToString();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e.GetType());
                    Debug.LogError($"Error Converting C# to JSON {e.Message}");
                    EditorUtility.DisplayDialog("Error", "Failed to convert C# to JSON. Check the console for details.","OK");
                }
            }

            private void BeautifyJson()
            {
                try
                {
                    var parsedJson = JsonConvert.DeserializeObject(_jsonText);
                    _jsonText = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error beautifying JSON: {e.Message}");
                    EditorUtility.DisplayDialog("Error", "Failed to beautify JSON. Check the console for details.","OK");
                }
            }
        }
        
        public static class CSharpScriptExecutorRoslyn
        {
            public static bool TryInitialize;
            public static object CreateInstance(string code)
            {
                var mainType = CompileCode(code);
                if (mainType == null)
                    throw new Exception("No Main class found in the provided code");

                var instance = Activator.CreateInstance(mainType);
                if (TryInitialize)
                    InitializeProperties(instance);
                return instance;
            }
            public static object[] CreateInstances(string code)
            {
                //INFO returning all instances and serializing all objects. 
                List<object> objects = new List<object>();
                var allTypes = CompileCodeAndReturnAllTypes(code);
                foreach (var type in allTypes)
                {
                    var temp = Activator.CreateInstance(type);
                    objects.Add(temp);
                    
                    if (TryInitialize)
                        InitializeProperties(temp);
                }

                return objects.ToArray();
            }
            private static Type CompileCode(string code)
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var references = new List<MetadataReference>();

                var folderPath  = EditorApplication.applicationContentsPath + @"\Managed\UnityEngine";
                var engineDlls = Directory.GetFiles(folderPath, "*.dll");

                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(EditorApplication.applicationContentsPath +
                                                                @"\UnityReferenceAssemblies\unity-4.8-api\Facades\netstandard.dll"));
                references.AddRange(engineDlls.Select(engineDll => MetadataReference.CreateFromFile(engineDll)));
                references.Add(MetadataReference.CreateFromFile(typeof(JsonConvert).Assembly.Location));
                
                var compilation = CSharpCompilation.Create(
                    assemblyName: "RoslynCompileSample",
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                var mainClass = (syntaxTree.GetRoot() as CompilationUnitSyntax)?.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>().FirstOrDefault()
                    ?.Identifier.Text;

                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (var diagnostic in failures)
                    {
                        Debug.LogError($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                    }

                    throw new Exception("Compilation Failed");
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                return assembly.GetType(mainClass);
            }

            private static Type[] CompileCodeAndReturnAllTypes(string code)
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var references = new List<MetadataReference>();

                var folderPath  = EditorApplication.applicationContentsPath + @"\Managed\UnityEngine";
                var engineDlls = Directory.GetFiles(folderPath, "*.dll");

                references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                references.Add(MetadataReference.CreateFromFile(EditorApplication.applicationContentsPath +
                                                                @"\UnityReferenceAssemblies\unity-4.8-api\Facades\netstandard.dll"));
                references.AddRange(engineDlls.Select(engineDll => MetadataReference.CreateFromFile(engineDll)));
                references.Add(MetadataReference.CreateFromFile(typeof(JsonConvert).Assembly.Location));
                
                var compilation = CSharpCompilation.Create(
                    assemblyName: "RoslynCompileSample",
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );

                var allClassDecls = (syntaxTree.GetRoot() as CompilationUnitSyntax)?.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>().Select(x => x.Identifier.Text);

                var allStructDecls = (syntaxTree.GetRoot() as CompilationUnitSyntax)?.DescendantNodes()
                    .OfType<StructDeclarationSyntax>().Select(x => x.Identifier.Text);
                
                using var ms = new MemoryStream();
                var result = compilation.Emit(ms);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (var diagnostic in failures)
                    {
                        Debug.LogError($"{diagnostic.Id}: {diagnostic.GetMessage()}");
                    }

                    throw new Exception("Compilation Failed");
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                List<Type> allTypes = new List<Type>();
                
                if (allClassDecls != null) 
                    allTypes.AddRange(allClassDecls.Select(classDecl => assembly.GetType(classDecl)));
                if (allStructDecls != null)
                    allTypes.AddRange(allStructDecls.Select(decl => assembly.GetType(decl)));

                return allTypes.ToArray();
            }
            
            private static Type FindMainType(IReadOnlyList<Type> types)
            {
                if (types.Count == 1)
                    return types[0];
                
                foreach (var type in types)
                {
                    var properties = type.GetProperties();
                    if (properties.Any(p => types.Contains(p.PropertyType)))
                        return type;
                }

                return types.FirstOrDefault(t => !t.IsNested);
            }
            
            private static void InitializeProperties(object instance)
            {
                var type = instance.GetType();
                foreach (var prop in type.GetProperties())
                {
                    if (!prop.CanWrite) continue;

                    Debug.Log($"{prop.PropertyType} -- {prop.Name}");
                    
                    object value = GetSampleValue(prop.PropertyType);
                    if (value == null)
                    {
                        continue;
                    }
                    prop.SetValue(instance, value);
                }
            }
            
            private static object GetSampleValue(Type type)
            {
                if (type == typeof(string))
                    return "Sample String";
                if (type == typeof(int))
                    return 42;
                if (type == typeof(float))
                    return 3.14f;
                if (type == typeof(double))
                    return 3.14159;
                if (type == typeof(bool))
                    return false;
                if (type == typeof(DateTime))
                    return DateTime.Now;
                if (type == typeof(Guid))
                    return Guid.NewGuid();
/*
                if (type.IsGenericType)
                {
                    //Debug.Log("GENERIC TYPE");
                    if (type.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        var listType = typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]);
                        var list = (IList) Activator.CreateInstance(listType);
                        var elementType = type.GetGenericArguments()[0];

                        for (int i = 0; i < 3; i++)
                        {
                            var element = GetSampleValue(elementType, depth + 1);
                            list.Add(element);
                        }

                        return list;
                    }
                }
              */
                //if (type.IsClass /*&& type != typeof(string)*/ )
                //{
                //    var nestedInstance = Activator.CreateInstance(type);
                //    InitializeProperties(nestedInstance, depth + 1);
                //    return nestedInstance;
                //}
                
                return null;
            }
        }
    }
    
    [Serializable]
    public class TestWithAttributes
    {
        [JsonProperty("first")] public int First { get; set; }
        [JsonProperty("second")] public string Second { get; set; }
        [JsonProperty("third")] public float Third { get; set; }
    }
    
    public class Vector3Converter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var vector = (Vector3)value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var jsonObject = JObject.Load(reader);
            var x = (jsonObject["x"] ?? 0.0f).Value<float>();
            var y = (jsonObject["y"] ?? 0.0f).Value<float>();
            var z = (jsonObject["z"] ?? 0.0f).Value<float>();
            return new Vector3(x, y, z);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector3);
        }
    }

    public class QuaternionConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var quaternion = (Quaternion) value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(quaternion.x);
            writer.WritePropertyName("y");
            writer.WriteValue(quaternion.y);
            writer.WritePropertyName("z");
            writer.WriteValue(quaternion.z);
            writer.WritePropertyName("w");
            writer.WriteValue(quaternion.w);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var jsonObject = JObject.Load(reader);
            var x = (jsonObject["x"] ?? 0.0f).Value<float>();
            var y = (jsonObject["y"] ?? 0.0f).Value<float>();
            var z = (jsonObject["z"] ?? 0.0f).Value<float>();
            var w = (jsonObject["w"] ?? 0.0f).Value<float>();

            return new Quaternion(x, y, z, w);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Quaternion);
        }
    }
}
/*
 *
using UnityEngine;
using System.Collections.Generic;

public class MC
{
    public SC Sc { get; set; }
}

public class SC
{
    public string SceneName { get; set; }
    public List<GO> GameObjects { get; set; }
}

public class GO
{
    public string Name { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Scale { get; set; }
}

public class Person
{
    public string Name { get; set; }
    public int Score { get; set; }
    public Address MyAddress { get; set; }
}

public class Address
{
    public string Street { get; set; }
}
 * 
 */