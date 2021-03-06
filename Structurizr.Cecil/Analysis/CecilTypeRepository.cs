﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Structurizr.Cecil;

namespace Structurizr.Analysis
{
    public class CecilTypeRepository : ITypeRepository
    {

        private readonly AssemblyDefinition _assembly;
        private readonly string _namespace;
        private readonly HashSet<Regex> _exclusions = new HashSet<Regex>();
        private readonly Dictionary<string, TypeDefinition> _types = new Dictionary<string, TypeDefinition>();
        private readonly Dictionary<string, IEnumerable<string>> _referencedTypesCache = new Dictionary<string, IEnumerable<string>>();

        /// <inheritdoc />
        public string Namespace { get { return _namespace; } }

        public CecilTypeRepository(AssemblyDefinition assembly, string namespaceName, HashSet<Regex> exclusions)
        {
            _assembly = assembly;
            _namespace = namespaceName;

            if (exclusions != null)
            {
                _exclusions.UnionWith(exclusions);
            }

            IEnumerable<TypeDefinition> types = from a in assembly.EnumerateReferencedAssemblies()
                                                from m in a.Modules
                                                from t in m.Types
                                                where InNamespace(t)
                                                select t;

            foreach (TypeDefinition type in types)
            {
                string assemblyQualifiedName = type.GetAssemblyQualifiedName();
                if (assemblyQualifiedName != null)
                {
                    _types.Add(assemblyQualifiedName, type);
                }
            }
        }

        private bool InNamespace(TypeReference type)
        {
            return type.Namespace != null
                && (
                    type.Namespace == _namespace
                    || string.IsNullOrEmpty(_namespace)
                    || type.Namespace.StartsWith(_namespace + ".")
                );
        }

        /// <inheritdoc />
        public IEnumerable<TypeDefinition> GetAllTypes()
        {
            return _types.Values;
        }

        /// <inheritdoc />
        public IEnumerable<string> GetReferencedTypes(string typeName)
        {
            // use the cached version if possible
            if (_referencedTypesCache.ContainsKey(typeName))
            {
                return _referencedTypesCache[typeName];
            }

            HashSet<string> referencedTypes = new HashSet<string>();
            TypeDefinition type;
            if (_types.TryGetValue(typeName, out type) && type != null)
            {
                foreach (PropertyDefinition propertyDefinition in type.Properties)
                {
                    AddReferencedTypeIfNotExcluded(propertyDefinition.PropertyType, referencedTypes);
                }

                foreach (FieldDefinition fieldDefinition in type.Fields)
                {
                    AddReferencedTypeIfNotExcluded(fieldDefinition.FieldType, referencedTypes);
                }

                foreach (MethodDefinition methodDefinition in type.Methods)
                {
                    AddReferencedTypeIfNotExcluded(methodDefinition.ReturnType, referencedTypes);

                    foreach (ParameterDefinition parameterDefinition in methodDefinition.Parameters)
                    {
                        AddReferencedTypeIfNotExcluded(parameterDefinition.ParameterType, referencedTypes);
                    }

                    MethodBody methodBody = methodDefinition.Body;
                    if (methodBody != null)
                    {
                        foreach (VariableDefinition variableDefinition in methodBody.Variables)
                        {
                            // TODO: skip variables where type is marked with CompilerGeneratedAttribute
                            AddReferencedTypeIfNotExcluded(variableDefinition.VariableType, referencedTypes);
                        }
                    }
                }
            }

            // cache for next time
            _referencedTypesCache[typeName] = referencedTypes;

            return referencedTypes;
        }

        private void AddReferencedTypeIfNotExcluded(TypeReference type, HashSet<string> referencedTypes)
        {
            var assemblyQualifiedName = type.GetAssemblyQualifiedName();
            if (assemblyQualifiedName != null)
            {
                if (!IsExcluded(assemblyQualifiedName))
                {
                    referencedTypes.Add(assemblyQualifiedName);
                }

                if (type.IsGenericInstance)
                {
                    var genericInstance = (GenericInstanceType)type;
                    foreach (TypeReference genericArgumentType in genericInstance.GenericArguments)
                    {
                        AddReferencedTypeIfNotExcluded(genericArgumentType, referencedTypes);
                    }
                }
            }
        }

        /// <inheritdoc />
        public string FindVisibility(string typeName)
        {
            TypeDefinition type = _types[typeName];
            if (type != null)
            {
                if (type.IsPublic)
                {
                    return "public";
                }
                else if (type.IsNestedAssembly)
                {
                    return "internal";
                }
            }

            // todo
            return null;
        }

        /// <inheritdoc />
        public string FindCategory(string typeName)
        {
            TypeDefinition type = _types[typeName];
            if (type != null)
            {
                if (type.IsAbstract)
                {
                    if (type.IsSealed)
                    {
                        return "static class";
                    }

                    return "abstract class";
                }
                else if (type.IsInterface)
                {
                    return "interface";
                }
                else if (type.IsEnum)
                {
                    return "enum";
                }
            }

            // todo
            return null;
        }

        private bool IsExcluded(string typeName)
        {
            foreach (Regex exclude in _exclusions)
            {
                if (exclude.IsMatch(typeName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
