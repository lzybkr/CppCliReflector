using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Reflector;
using Reflector.CodeModel;
using System.Globalization;

namespace Reflector.Languages
{
// TODO:
// generic<> should only appear on the individual methods, not foreach method within a class
// WriteType should use a GetName syntax.
// using (int i) {} does not work.
//

    class CppCliLanguagePackage : IPackage
	{
        public virtual void Load(IServiceProvider serviceProvider)
        {
        	languageManager = serviceProvider.GetService(typeof(ILanguageManager)) as ILanguageManager;
	        cppCliLanguage = new CppCliLanguage();
	        languageManager.RegisterLanguage(cppCliLanguage);
        }
        public virtual void Unload()
        {
            languageManager.UnregisterLanguage(cppCliLanguage);
        }

		ILanguageManager languageManager;
		CppCliLanguage cppCliLanguage;
	};

    struct MethodNameExt
    {
	    string N;
	    string OriginalN;
	    ICollection O;
	    public bool Constructor;
        public bool StaticConstructor;
        public bool ImplicitOrExplicit;
        public bool Explicit;

	    public MethodNameExt(string Name, ICollection Overrides, string TypeName)
	    {
            Constructor = false;
            StaticConstructor = false;
            ImplicitOrExplicit = false;
            Explicit = false;

		    if (Name == "op_Explicit")
		    {
			    ImplicitOrExplicit = true;
			    Explicit = true;
		    } 
		    else if (Name == "op_Implicit")
		    {
			    ImplicitOrExplicit = true;
		    } 
		    else if (Name == ".ctor")
		    {
			    Name = TypeName;
			    Constructor = true;
		    }
		    else if (Name == ".cctor")
		    {
			    Name = TypeName;
			    StaticConstructor = true;
		    }
		    OriginalN = Name;
		    O = Overrides;
            N = null;
	    }

	    public string Name
	    {
		    get
		    {
			    if (N==null)
			    {
				    N = OriginalN;
				    if (!Constructor)
				    {
					    if (O==null || O.Count > 0)
					    {
						    N = N.Replace(".", "_");		// C# explicit overrides have names that can't be used in C++, so we tweak them here
					    }

                        string name;
                        if (CppCliLanguage.LanguageWriter.specialMethodNames.TryGetValue(N, out name))
                        {
                            N = name;
                        }
				    }
			    }
			    return N;
		    }
	    }
    }


    class CppCliLanguage : ILanguage
    {
        // Methods
        internal CppCliLanguage()
        {
        }

        public ILanguageWriter GetWriter(IFormatter formatter, ILanguageWriterConfiguration configuration)
        {
	        return new LanguageWriter(formatter, configuration);
        }

        public string FileExtension { get { return "cpp"; } }
        public string Name { get {	return "C++/CLI"; } }
        public bool Translate { get { return true; } }

        internal class LanguageWriter : ILanguageWriter
        {
            static internal Dictionary<string, string> specialMethodNames;
            static internal Dictionary<string, string> specialTypeNames;

			internal Hashtable refOnStack;					//
			bool SuppressOutput;					//
			bool SomeConstructor;					//
			string[] ExtraTemporaries;		//extra temporaries
			string[] ExtraMappings;			//what they are mapped to.
			bool[] EssentialTemporaries;		//we need these.
			int[] TemporaryBlocks;			//where we saw them
			int Block;								//block number for extra temporaries
			int NextBlock;							//the next block we see.
			ILanguageWriterConfiguration configuration;
			IFormatter formatter;
			ITypeReference baseType;
			int SkipTryCount;						//count of elements in Try block we handled already
			int SkipNullptrCount;
			bool SkipWriteLine;						//don't need a writeline, like inside a for


            static LanguageWriter()
            {
	            specialTypeNames = new Dictionary<string, string>();
	            specialTypeNames["Void"] = "void";
	            specialTypeNames["Boolean"] = "bool";
	            specialTypeNames["Char"] = "wchar_t";
	            specialTypeNames["SByte"] = "char";
	            specialTypeNames["Byte"] = "unsigned char";
	            specialTypeNames["Int16"] = "short";
	            specialTypeNames["ushort"] = "ushort";
	            specialTypeNames["Int32"] = "int";
	            specialTypeNames["UInt32"] = "uint";
	            specialTypeNames["Int64"] = "long";
	            specialTypeNames["UInt64"] = "ulong";
	            specialTypeNames["Single"] = "float";
	            specialTypeNames["Double"] = "double";

	            specialMethodNames = new Dictionary<string, string>();

	            // CLS-compliant unary operators
	            specialMethodNames["op_AddressOf"] = "operator & ";
	            specialMethodNames["op_LogicalNot"] = "operator ! ";
	            specialMethodNames["op_OnesComplement"] = "operator ~ ";
	            specialMethodNames["op_PointerDereference"] = "operator * ";
	            specialMethodNames["op_UnaryNegation"] = "operator - ";
	            specialMethodNames["op_UnaryPlus"] = "operator + ";

	            // CLS-Compliant binary operators
	            specialMethodNames["op_MemberSelection"] = "operator . ";
	            specialMethodNames["op_Addition"] = "operator + ";
	            specialMethodNames["op_BitwiseAnd"] = "operator & ";
	            specialMethodNames["op_BitwiseOr"] = "operator | ";
	            specialMethodNames["op_ExclusiveOr"] = "operator  ";
	            specialMethodNames["op_Increment"] = "operator ++ ";
	            specialMethodNames["op_Subtraction"] = "operator - ";
	            specialMethodNames["op_Decrement"] = "operator -- ";
	            specialMethodNames["op_Multiply"] = "operator * ";
	            specialMethodNames["op_Division"] = "operator / ";
	            specialMethodNames["op_Modulus"] = "operator % ";
	            specialMethodNames["op_Negation"] = "operator ! ";
	            specialMethodNames["op_LeftShift"] = "operator << ";
	            specialMethodNames["op_RightShift"] = "operator >> ";
	            specialMethodNames["op_Equality"] = "operator == ";
	            specialMethodNames["op_Inequality"] = "operator != ";
	            specialMethodNames["op_GreaterThanOrEqual"] = "operator >= ";
	            specialMethodNames["op_LessThanOrEqual"] = "operator <= ";
	            specialMethodNames["op_GreaterThan"] = "operator > ";
	            specialMethodNames["op_LessThan"] = "operator < ";
	            specialMethodNames["op_True"] = "operator true ";
	            specialMethodNames["op_False"] = "operator false ";
	            specialMethodNames["op_Implicit"] = "implicit ";
	            specialMethodNames["op_Explicit"] = "explicit ";
            }
 
            internal LanguageWriter(IFormatter formatter, ILanguageWriterConfiguration configuration)
            {
	            this.formatter = formatter;
	            this.configuration = configuration;
            }

            public void WriteAssembly(IAssembly assembly)
            {
	            this.Write("// Assembly");
	            this.Write(" ");
	            this.WriteDeclaration(assembly.Name);
	            if (assembly.Version != null)
	            {
		            this.Write(", ");
		            this.Write("Version");
		            this.Write(" ");
		            this.Write(assembly.Version.ToString());
	            }
	            this.WriteLine();
	            if ((this.configuration["ShowCustomAttributes"] == "true") && (assembly.Attributes.Count != 0))
	            {
		            this.WriteLine();
		            //this.WriteCustomAttributeCollection(this.formatter, assembly);
		            this.WriteLine();
	            }
	            this.WriteProperty("Location", assembly.Location);
	            this.WriteProperty("Name", assembly.ToString());
            }

            public void WriteAssemblyReference(IAssemblyReference assemblyReference)
            {
	            this.Write("// Assembly Reference");
	            this.Write(" ");
	            this.WriteDeclaration(assemblyReference.Name);
	            this.WriteLine();
	            this.WriteProperty("Version", assemblyReference.Version.ToString());
	            this.WriteProperty("Name", assemblyReference.ToString());
            }

            void WriteExpressionCollection(IExpressionCollection iExpressionCollection)
            {
	            string separator = "";
	            foreach (IExpression iExpression in iExpressionCollection)
	            {
		            this.Write(separator);
		            this.WriteExpression(iExpression);
		            separator = ", ";
	            }
            }

            void TypeWriter(StringWriter writer, IType iType)
            {
	            writer.Write(GetNameFromType(iType));
            }

            string GetNameFromTypeReference(ITypeReference iTypeReference)
            {
	            string s = iTypeReference.Name;
                string s1;
	            if (iTypeReference.Namespace == "System" && specialTypeNames.TryGetValue(s, out s1))
	            {
                    s = s1;
	            }

	            ITypeCollection genericParameters = iTypeReference.GenericArguments;
	            if (genericParameters.Count > 0)
	            {
		            string separator = "";
		            s += "<";
		            foreach(IType iType in genericParameters)
		            {
			            s += separator;
			            separator = ",";
			            s += GetNameFromType(iType);
		            }
		            s += ">";
	            }
	            return s.Replace(".", ".").Replace("+", ".");
            }

            string GetNameFromType(IType iType)
            {
                IOptionalModifier iOptionalModifier = (iType) as IOptionalModifier;
	            if (iOptionalModifier != null)
	            {
		            string s = GetNameFromType(iOptionalModifier.ElementType);
		            if (iOptionalModifier.Modifier.Name == "IsLong" && iOptionalModifier.Modifier.Namespace == "System.Runtime.CompilerServices")
		            {
			            if (s == "int")
			            {
				            s = "long";
			            }
		            }
		            else
		            {
			            s += " " + iOptionalModifier.Modifier.ToString();
		            }
		            return s;
	            }

                ITypeReference iTypeReference = (iType) as ITypeReference;
	            if (iTypeReference != null)
	            {
		            return GetNameFromTypeReference(iTypeReference);
	            }
	            return iType.ToString();
            }

            void WriteTypeReference(ITypeReference typeReference)
            {
	            string name = GetNameFromTypeReference(typeReference);
	            string s = typeReference.Namespace + "." + typeReference.Name;
	            s = s.Replace(".", ".").Replace("+", ".");
	            this.WriteReference(name, s, typeReference);
            }

            void WriteType(IType type)
            {
                WriteType<object>(type, null, null, null, false);
            }

            void WriteType<T>(IType type, Action<T> callback, T middle, ICustomAttributeProvider iCustomAttributeProvider, bool fRefOnStack)
            {
	            if ((this.configuration["ShowCustomAttributes"] == "true") && iCustomAttributeProvider != null)
	            {
		            this.WriteCustomAttributeCollection(iCustomAttributeProvider, type);
	            }

                ITypeReference tagType;
                IArrayType arrayType;
                IPointerType pointerType;
                IReferenceType refType;
                IOptionalModifier modoptType;
                IRequiredModifier modreqType;
                IFunctionPointer funcPtrType;
                IGenericParameter genericParam;
                IGenericArgument genericArg;
                IValueTypeConstraint iValueTypeConstraint;
                IDefaultConstructorConstraint iDefaultConstructorConstraint;

	            if ((tagType = type as ITypeReference) != null)
	            {
		            this.WriteTypeReference(tagType);
		            if (!Helper.IsValueType(tagType) && !fRefOnStack)
		            {
			            this.Write("");
		            }
	            }
	            else if ((arrayType = type as IArrayType) != null)
	            {
		            this.Write("array<");
		            this.WriteType(arrayType.ElementType);
		            if (arrayType.Dimensions.Count > 1)
		            {
			            this.Write(", " + arrayType.Dimensions.Count.ToString());
		            }
		            this.Write(">");
	            }
	            else if ((pointerType = type as IPointerType) != null)
	            {
		            this.WriteType(pointerType.ElementType);
		            this.Write("*");
	            }
	            else if ((refType = type as IReferenceType) != null)
	            {
		            this.WriteType(refType.ElementType);
		            this.Write("%");
	            }
            #if false
	            else if ((pinType = type as IPinnedType) != null)
	            {
		            this.Write("pin_ptr<");
		            this.WriteType(pinType.ElementType);
		            this.Write(">");
	            }
            #endif
	            else if ((modoptType = type as IOptionalModifier) != null)
	            {
		            WriteModifiedType(modoptType.Modifier, modoptType.ElementType, false, fRefOnStack);
	            }
	            else if ((modreqType = type as IRequiredModifier) != null)
	            {
		            WriteModifiedType(modreqType.Modifier, modreqType.ElementType, true, fRefOnStack);
	            }
	            else if ((funcPtrType = type as IFunctionPointer) != null)
	            {
		            this.Write("funcptr");
		            /*
                                                            this.WriteType(pointer1.ReturnType.Type, formatter, false);
                                                            this.Write(" *(");
                                                            for (int num2 = 0; num2 < pointer1.Parameters.Count; num2++)
                                                            {
                                                                if (num2 != 0)
                                                                {
                                                                    this.Write(", ");
                                                                }
                                                                this.WriteType(pointer1.Parameters[num2].ParameterType, formatter, false);
                                                            }
                                                            this.Write(")");
		            */
	            }
	            else if ((genericParam = type as IGenericParameter) != null)
	            {
		            this.Write(genericParam.Name);
	            }
	            else if ((genericArg = type as IGenericArgument) != null)
	            {
		            this.WriteType(genericArg.Resolve());
	            }
	            else if ((iValueTypeConstraint = type as IValueTypeConstraint) != null)
	            {
		            this.WriteKeyword("value");
		            this.WriteKeyword(" ");
		            this.WriteKeyword("class");
	            }
	            else if ((iDefaultConstructorConstraint = type as IDefaultConstructorConstraint) != null)
	            {
                    this.Write("TODO #1");
	            }
	            else
	            {
		            this.Write("WHAT TYPE IS IT?");
	            }

	            if (callback != null)
	            {
		            this.Write(" ");
		            callback(middle);
	            }
            }

            public void WriteExpression(IExpression expression)
            {
	            if (expression==null)
	            {
		            return;
	            }

	            IPropertyIndexerExpression iPropertyIndexerExpression;
	            IDelegateCreateExpression iDelegateCreateExpression;
	            ITypeOfExpression iTypeOfExpression;
	            ISnippetExpression iSnippetExpression;
	            IArrayIndexerExpression iArrayIndexerExpression;
                //IObjectInitializeExpression iObjectInitializeExpression;
	            IAddressDereferenceExpression iAddressDereferenceExpression;
	            IAddressOfExpression iAddressOfExpression;
	            IAddressOutExpression iAddressOutExpression;
	            IAddressReferenceExpression iAddressReferenceExpression;
	            IArgumentListExpression iArgumentListExpression;
	            IUnaryExpression iUnaryExpression;
	            IBinaryExpression iBinaryExpression;
	            IBaseReferenceExpression iBaseReferenceExpression;
	            ITypeReferenceExpression iTypeReferenceExpression;
	            IObjectCreateExpression iObjectCreateExpression;
	            ICastExpression iCastExpression;
	            ILiteralExpression literalExpression;
	            IMethodInvokeExpression  iMethodInvokeExpression;
	            IMethodReferenceExpression iMethodReferenceExpression;
	            IVariableDeclarationExpression iVariableDeclarationExpression;
	            IVariableReferenceExpression iVariableReferenceExpression;
	            IArgumentReferenceExpression iArgumentReferenceExpression;
	            //IArrayInitializerExpression iArrayInitializerExpression;
	            IThisReferenceExpression iThisReferenceExpression;
	            IPropertyReferenceExpression iPropertyReferenceExpression;
	            IArrayCreateExpression iArrayCreateExpression;
	            IConditionExpression iConditionExpression;
	            IFieldReferenceExpression iFieldReferenceExpression;
	            //INamedArgumentExpression iNamedArgumentExpression;
            #if false
	            IStatementExpression iStatementExpression;
            #endif
	            ICanCastExpression iCanCastExpression;
	            ITryCastExpression iTryCastExpression;
	            IAssignExpression iAssignExpression;

	            if ((iPropertyIndexerExpression = expression as IPropertyIndexerExpression) != null)
	            {
		            WritePropertyIndexerExpression(iPropertyIndexerExpression);
                    return;
	            }
	            if ((iDelegateCreateExpression = expression as IDelegateCreateExpression) != null)
	            {
		            WriteDelegateCreateExpression(iDelegateCreateExpression);
                    return;
	            }
	            if ((iTypeOfExpression = expression as ITypeOfExpression) != null)
	            {
		            WriteTypeOfExpression(iTypeOfExpression);
                    return;
                }
	            if ((iSnippetExpression = expression as ISnippetExpression) != null)
	            {
		            WriteSnippetExpression(iSnippetExpression);
                    return;
                }
	            if ((iArrayIndexerExpression = expression as IArrayIndexerExpression) != null)
	            {
		            WriteArrayIndexerExpression(iArrayIndexerExpression);
                    return;
                }
	            //if ((iObjectInitializeExpression = expression as IObjectInitializeExpression) != null)
	            //{
		        //    WriteObjectInitializeExpression(iObjectInitializeExpression);
                //    return;
                //}
	            if ((iAddressDereferenceExpression = expression as IAddressDereferenceExpression) != null)
	            {
		            WriteAddressDereferenceExpression(iAddressDereferenceExpression);
                    return;
                }
	            if ((iAddressOfExpression = expression as IAddressOfExpression) != null)
	            {
		            WriteAddressOfExpression(iAddressOfExpression);
                    return;
                }
	            if ((iAddressOutExpression = expression as IAddressOutExpression) != null)
	            {
		            WriteAddressOutExpression(iAddressOutExpression);
                    return;
                }
	            if ((iAddressReferenceExpression = expression as IAddressReferenceExpression) != null)
	            {
		            WriteAddressReferenceExpression(iAddressReferenceExpression);
                    return;
                }
	            if ((iArgumentListExpression = expression as IArgumentListExpression) != null)
	            {
		            WriteArgumentListExpression(iArgumentListExpression);
                    return;
                }
	            if ((iUnaryExpression = expression as IUnaryExpression) != null)
	            {
		            WriteUnaryExpression(iUnaryExpression);
                    return;
                }
	            if ((iBinaryExpression = expression as IBinaryExpression) != null)
	            {
		            WriteBinaryExpression(iBinaryExpression);
                    return;
                }
	            if ((iBaseReferenceExpression = expression as IBaseReferenceExpression) != null)
	            {
		            WriteBaseReferenceExpression(iBaseReferenceExpression);
                    return;
                }
	            if ((iTypeReferenceExpression = expression as ITypeReferenceExpression) != null)
	            {
		            WriteTypeReferenceExpression(iTypeReferenceExpression);
                    return;
                }
	            if ((iObjectCreateExpression = expression as IObjectCreateExpression) != null)
	            {
		            WriteObjectCreateExpression(iObjectCreateExpression);
                    return;
                }
	            if ((iCastExpression = expression as ICastExpression) != null)
	            {
		            WriteCastExpression(iCastExpression);
                    return;
                }
	            if ((literalExpression = expression as ILiteralExpression) != null)
	            {
		            WriteLiteralExpression(literalExpression);
                    return;
                }
	            if ((iMethodInvokeExpression = expression as IMethodInvokeExpression) != null)
	            {
		            WriteMethodInvokeExpression(iMethodInvokeExpression);
                    return;
                }
	            if ((iMethodReferenceExpression = expression as IMethodReferenceExpression) != null)
	            {
		            WriteMethodReferenceExpression(iMethodReferenceExpression);
                    return;
                }
	            if ((iVariableDeclarationExpression = expression as IVariableDeclarationExpression) != null)
	            {
		            WriteVariableDeclaration(iVariableDeclarationExpression.Variable);
                    return;
                }
	            if ((iVariableReferenceExpression = expression as IVariableReferenceExpression) != null)
	            {
		            WriteVariableReferenceExpression(iVariableReferenceExpression);
                    return;
                }
	            if ((iArgumentReferenceExpression = expression as IArgumentReferenceExpression) != null)
	            {
		            WriteArgumentReferenceExpression(iArgumentReferenceExpression);
                    return;
                }
	            //if ((iArrayInitializerExpression = expression as IArrayInitializerExpression) != null)
	            //{
		        //    WriteArrayInitializerExpression(iArrayInitializerExpression);
                //    return;
                //}
	            if ((iThisReferenceExpression = expression as IThisReferenceExpression) != null)
	            {
		            WriteThisReferenceExpression(iThisReferenceExpression);
                    return;
                }
	            if ((iPropertyReferenceExpression = expression as IPropertyReferenceExpression) != null)
	            {
		            WritePropertyReferenceExpression(iPropertyReferenceExpression);
                    return;
                }
	            if ((iArrayCreateExpression = expression as IArrayCreateExpression) != null)
	            {
		            WriteArrayCreateExpression(iArrayCreateExpression);
                    return;
                }
	            if ((iConditionExpression = expression as IConditionExpression) != null)
	            {
		            WriteConditionExpression(iConditionExpression);
                    return;
                }
	            if ((iFieldReferenceExpression = expression as IFieldReferenceExpression) != null)
	            {
		            WriteFieldReferenceExpression(iFieldReferenceExpression);
                    return;
                }
	            //if ((iNamedArgumentExpression = expression as INamedArgumentExpression) != null)
	            //{
		        //    WriteNamedArgumentExpression(iNamedArgumentExpression);
                //    return;
                //}
            #if false
	            if ((iStatementExpression = expression as IStatementExpression) != null)
	            {
		            WriteStatementExpression(iStatementExpression);
                    return;
	            }
            #endif
                if ((iCanCastExpression = expression as ICanCastExpression) != null)
	            {
		            WriteCanCastExpression(iCanCastExpression);
                    return;
                }
	            if ((iTryCastExpression = expression as ITryCastExpression) != null)
	            {
		            WriteTryCastExpression(iTryCastExpression);
                    return;
                }
	            if ((iAssignExpression = expression as IAssignExpression) != null)
	            {
		            WriteAssignExpression(iAssignExpression);
                    return;
                }

	            // we already checked for expression==null above
	            this.Write(expression.ToString());
            }

            void WriteLiteralExpression(ILiteralExpression literalExpression)
            {
	            Object value = literalExpression.Value;
	            if (value is bool)
	            {
		            bool val = (bool)(value);
		            this.WriteLiteral(val ? "true" : "false");
                    return;
	            }
	            if (value is char)
	            {
		            this.Write("'");
                    this.WriteOneChar((char)value);
		            this.Write("'");
		            return;
	            }
	            if (value is byte)
	            {
		            byte num = (byte)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
	            }
	            if (value is sbyte)
	            {
		            sbyte num = (sbyte)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is short)
	            {
		            short num = (short)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is ushort)
	            {
		            ushort num = (ushort)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is int)
	            {
		            int num = (int)(value);
		            uint u = (uint)num;
		            if (u>= 0x80000000 && u<=0x8fffffff)		//Error codes as hex
		            {
			            this.WriteLiteral("0x" + num.ToString("X8"));
		            }
		            else
		            {
			            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
		            }
                    return;
	            }
	            if (value is uint)
	            {
		            uint u = (uint)(value);
		            if (u>= 0x80000000)						//probably hex
		            {
			            this.WriteLiteral("0x" + u.ToString("X8"));
		            }
		            else
		            {
			            this.WriteLiteral(u.ToString(CultureInfo.InvariantCulture));
		            }
                    return;
                }
	            if (value is long)
	            {
		            long num = (long)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is ulong)
	            {
		            ulong num = (ulong)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is float)
	            {
		            float num = (float)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture)+"f");
                    return;
                }
	            if (value is double)
	            {
		            double num = (double)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value is decimal)
	            {
		            decimal num = (decimal)(value);
		            this.WriteLiteral(num.ToString(CultureInfo.InvariantCulture));
                    return;
                }
	            if (value == null)
	            {
		            this.WriteLiteral("null");
                    return;
                }
                string s = value as string;
	            if (s != null)
	            {
		            this.Write("\"");
		            foreach(char w in s)
		            {
                        this.WriteOneChar(w);
		            }
		            this.Write("\"");
		            return;
	            }
	            if (value is byte[])
	            {
                    byte[] a = (byte[])value;
		            string separator="{ ";
		            for (int num1 = 0; num1 < a.Length; num1++)
		            {
			            this.Write(separator);
			            this.WriteLiteral("0x" + a[num1].ToString("X2"));
			            separator=",";
		            }
		            this.Write(" }");
		            return;
	            }
	            this.Write("Literal expression type NYI");
            }

            void WriteOneChar(char w)
            {
                if ((uint)w < 0x80)
                {
                    string toWrite;
                    switch (w)
                    {
                        case '\0': toWrite = "\\0"; break;
                        case '\r': toWrite = "\\r"; break;
                        case '\n': toWrite = "\\n"; break;
                        case '\t': toWrite = "\\t"; break;
                        case '\v': toWrite = "\\v"; break;
                        case '\b': toWrite = "\\b"; break;
                        default:
                            toWrite = w.ToString(CultureInfo.InvariantCulture);
                            break;
                    }
                    this.WriteLiteral(toWrite);
                }
                else
                {
                    uint num = (uint)w;
                    this.WriteLiteral("\\u" + num.ToString("X4"));
                }
            }

            // A simple callback to print out a name that might appear in the
            // middle of a type declaration
            void WriteName(string middle)
            {
	            try
	            {
		            if (middle != null)
		            {
			            this.Write(middle);
		            }
	            }
	            catch(Exception )
	            {
		            this.Write("exception deanwi");
	            }
            }

            public void WriteFieldDeclaration(IFieldDeclaration fieldDeclaration)
            {
	            WriteFieldVisibilitySpecifier(fieldDeclaration.Visibility);
	            if (fieldDeclaration.Literal)
	            {
		            this.WriteKeyword("literal");
		            this.Write(" ");
	            }
	            else if (fieldDeclaration.Static)
	            {
		            this.WriteKeyword("static");
		            this.Write(" ");
	            }
	            if (fieldDeclaration.ReadOnly)
	            {
		            this.WriteKeyword("initonly");
		            this.Write(" ");
	            }

	            this.WriteType(fieldDeclaration.FieldType, this.WriteName, fieldDeclaration.Name, null, false);

                IExpression initExpr = fieldDeclaration.Initializer;
	            if (initExpr != null)
	            {
		            this.Write(" = ");
		            this.WriteExpression(initExpr);
	            }

	            this.Write(";");
            }

            public void WriteMethodDeclaration(IMethodDeclaration methodDeclaration)
            {
	            bool OuterBlock = true;	//surround the method with {}. Needed because sometimes we have to emit the first part of a try block.
	            SkipTryCount = 0;
	            SomeConstructor = false;
	            refOnStack = new Hashtable();
	            IConstructorDeclaration  iConstructorDeclaration = (methodDeclaration) as IConstructorDeclaration;

	            // Need to check if we have a special function like the dtor or the finalizer
	            ITypeDeclaration declaringTypeDecl = (methodDeclaration.DeclaringType as ITypeReference).Resolve();
	            MethodNameExt MNE = new MethodNameExt(methodDeclaration.Name, methodDeclaration.Overrides, declaringTypeDecl.Name);
	            this.baseType = declaringTypeDecl.BaseType;
	            bool fGlobal = declaringTypeDecl.Name == "<Module>";
                bool firstThingAfterVisibility = true;
	            if (!fGlobal && !declaringTypeDecl.Interface && (!methodDeclaration.SpecialName || (methodDeclaration.Name != ".cctor")))
	            {
		            WriteMethodVisibilitySpecifier(methodDeclaration.Visibility);
                }
                if ((this.configuration["ShowCustomAttributes"] == "true") && (methodDeclaration.Attributes.Count != 0))
                {
                    this.WriteLine();
                    firstThingAfterVisibility = false;
                    this.WriteCustomAttributeCollection(methodDeclaration, null);
                    this.WriteLine();
                }
                WriteGenericArguments(declaringTypeDecl.GenericArguments, ref firstThingAfterVisibility);
                WriteGenericArguments(methodDeclaration.GenericArguments, ref firstThingAfterVisibility);

                if (firstThingAfterVisibility)
                {
                    this.Write(" ");
                    firstThingAfterVisibility = false;
                }
                
                if (!fGlobal && methodDeclaration.Static)
	            {
		            this.WriteKeyword("static");
		            this.Write(" ");
	            }
	            if (!declaringTypeDecl.Interface && methodDeclaration.Virtual)
	            {
                    this.WriteKeyword("virtual");
		            this.Write(" ");
	            }
	            if (MNE.Constructor || MNE.StaticConstructor)
	            {
                    this.SomeConstructor = true;
		            this.WriteMethodDeclMiddle(methodDeclaration);
	            }
	            else if (MNE.ImplicitOrExplicit)
	            {
		            if (MNE.Explicit)
		            {
			            this.WriteKeyword("explicit");
			            this.Write(" ");
		            }
		            this.WriteKeyword("operator");
		            this.Write(" ");
		            this.WriteType(methodDeclaration.ReturnType.Type);
		            this.Write("(");
		            this.Write(")");
	            }
	            else
	            {
		            this.WriteType(methodDeclaration.ReturnType.Type,
			            this.WriteMethodDeclMiddle, methodDeclaration, methodDeclaration.ReturnType, false);
	            }
	
	            // new,sealed,abstract,override
	
	            if (!methodDeclaration.NewSlot && methodDeclaration.Final)
	            {
		            this.Write(" ");
		            this.WriteKeyword("sealed");
	            }
	            if (methodDeclaration.Virtual)
	            {
		            if (methodDeclaration.Abstract)
		            {
			            this.Write(" ");
			            this.WriteKeyword("abstract");
		            }
		
		            if (!methodDeclaration.NewSlot)
		            {
			            this.Write(" ");
			            this.WriteKeyword("override");
		            }

		            // How do we decide to write 'new' here?  If the method is introduced
		            // in the current class, it's marked newslot, but we only use the 'new'
		            // keyword when it would otherwise be an override.
	            }

	            // explicit override list
	            ICollection explicitOverrides = methodDeclaration.Overrides;

	            if (explicitOverrides.Count > 0)
	            {
		            string separator = " = ";
		            foreach (IMethodReference overriddingMethod in explicitOverrides)
		            {
			            this.Write(separator);
			            this.WriteReference(overriddingMethod.Name, "", overriddingMethod);
			            separator = ", ";
		            }
	            }
	            this.WriteGenericParameterConstraintCollection(methodDeclaration.GenericArguments);
	            if (MNE.Constructor && iConstructorDeclaration != null)
	            {
                    IMethodInvokeExpression iMethodInvokeExpression = iConstructorDeclaration.Initializer;
		            if (iMethodInvokeExpression != null)
		            {
			            // R() : R1(1) {}
			            IMethodReferenceExpression iMethodReferenceExpression = iMethodInvokeExpression.Method as IMethodReferenceExpression;
			            if (iMethodReferenceExpression != null)
			            {
				            if (iMethodInvokeExpression.Arguments.Count > 0)
				            {
					            this.Write(" : ");
					            this.WriteExpression(iMethodReferenceExpression.Target);
					            this.Write("(");
					            this.WriteExpressionCollection(iMethodInvokeExpression.Arguments);
					            this.Write(")");
				            }
			            }
		            }
		            else
		            {
			            // maybe: R() try : R1(1)
			            IBlockStatement iBlockStatement = (methodDeclaration.Body) as IBlockStatement;
			            if (iBlockStatement != null)
			            {
				            IStatementCollection iStatementCollection = iBlockStatement.Statements;
				            GatherExtraTemporaries(iStatementCollection);	//gather up those temporaries, and set SkipnullCount
				            if (SkipNullptrCount < iStatementCollection.Count)
				            {
                                ITryCatchFinallyStatement iTryCatchFinallyStatement =
                                    iStatementCollection[SkipNullptrCount] as ITryCatchFinallyStatement;
					            if (iTryCatchFinallyStatement != null)
					            {
						            if (iTryCatchFinallyStatement.Try != null)
						            {
							            foreach (IStatement iStatement in iTryCatchFinallyStatement.Try.Statements)
							            {								
                                            IAssignExpression iAssignExpression = iStatement as IAssignExpression;
								            if (iAssignExpression != null)
								            {
									            IFieldReferenceExpression iFieldReferenceExpression = (iAssignExpression.Target) as IFieldReferenceExpression;
									            if (iFieldReferenceExpression != null)
									            {
										            SkipTryCount++;
										            continue;	//valid
									            }
								            }
								            else
								            {
									            IExpressionStatement iExpressionStatement = iStatement as IExpressionStatement;
									            if (iExpressionStatement != null)
									            {
										            IVariableDeclarationExpression iVariableDeclarationExpression = (iExpressionStatement.Expression) as IVariableDeclarationExpression;
										            if (iVariableDeclarationExpression != null)
										            {
											            SkipTryCount++;
											            continue;	//valid
										            }
										            IMethodInvokeExpression iMethodInvokeExpression1 = iExpressionStatement.Expression as IMethodInvokeExpression;
										            if (iMethodInvokeExpression1 != null)
										            {
											            IMethodReferenceExpression iMethodReferenceExpression = iMethodInvokeExpression1.Method as IMethodReferenceExpression;
											            if (iMethodReferenceExpression != null)
											            {
												            IBaseReferenceExpression iBaseReferenceExpression = iMethodReferenceExpression.Target as IBaseReferenceExpression;
												            if (iBaseReferenceExpression != null)
												            {
													            SkipTryCount++;
													            continue;
												            }
											            }
										            }
									            }
								            }
								            break;	//must be something else.
							            }
							            if (SkipTryCount > 0)
							            {
								            OuterBlock = false;	//there is no {} surrounding the entire method
								            this.WriteLine();
								            this.WriteIndent();	//indent, we're not used to seeing try on the left edge
								            this.Write("try : ");
								            string separator = "";
								            for (int i=0; i<SkipTryCount; i++)
								            {
									            this.Write(separator);
									            separator = "";
									            IStatement iStatement = iTryCatchFinallyStatement.Try.Statements[i];
									            IAssignExpression  iAssignExpression = (iStatement) as IAssignExpression;
									            if (iAssignExpression != null)
									            {
										            IFieldReferenceExpression iFieldReferenceExpression = (iAssignExpression.Target) as IFieldReferenceExpression;
										            this.Write(iFieldReferenceExpression.Field.Name);
										            this.Write("(");
										            this.WriteExpression(iAssignExpression.Expression);
										            this.Write(")");
										            separator = ", ";
									            } 
									            else
									            {
										            IExpressionStatement iExpressionStatement = iStatement as IExpressionStatement;
										            if (iExpressionStatement != null)
										            {
											            IMethodInvokeExpression iMethodInvokeExpression2 = iExpressionStatement.Expression as IMethodInvokeExpression;
											            if (iMethodInvokeExpression2 != null)
											            {
												            IMethodReferenceExpression iMethodReferenceExpression = iMethodInvokeExpression2.Method as IMethodReferenceExpression;
												            if (iMethodReferenceExpression != null)
												            {
													            IBaseReferenceExpression iBaseReferenceExpression = (iMethodReferenceExpression.Target) as IBaseReferenceExpression;
													            if (iBaseReferenceExpression != null)
													            {
														            this.WriteTypeReference(this.baseType);
														            this.Write("(");
														            this.WriteExpressionCollection(iMethodInvokeExpression2.Arguments);
														            this.Write(")");
														            separator = ", ";
													            }
												            }
											            }
										            }
									            }
								            }
							            }
						            }
					            }
				            }
			            }
		            }
	            }
	            this.WriteLine();
	            this.Write("{");
	            this.WriteLine();
	            this.WriteIndent();

                IBlockStatement iBlockStatement1 = methodDeclaration.Body as IBlockStatement;
	            if (iBlockStatement1 != null)
	            {
		            GatherExtraTemporaries(iBlockStatement1.Statements);	//greedily gather up those temporaries, and set SkipnullCount to their number.
		            if (SkipNullptrCount > 0)											//found some?								
		            {
                        SuppressOutput = true;
		        		int saveSkipTryCount = SkipTryCount;
			            try
                        {
                            NextBlock = 1;
    			            Block = NextBlock++;								//block number
		    	            this.WriteMethodBody(iBlockStatement1);				//we may have to regard some temporaries as essential due to mapping errors.
                        }
                        finally
                        {
                            ExtraMappings = new string[SkipNullptrCount];	//we need to reset this list now. mappings will be invalid
                            SuppressOutput = false;
                            SkipTryCount = saveSkipTryCount;
                        }
		            }
		            this.WriteMethodBody(iBlockStatement1);					//normal main output pass
	            }
	            this.WriteOutdent();
	            if (OuterBlock)
	            {
		            this.Write("}");
	            }
	            ExtraTemporaries = null;	//end of the scope of these temporaries
            }

            void WriteMethodBody(IBlockStatement iStatement)
            {
                if ((this.configuration["ShowMethodDeclarationBody"] == "true") && (iStatement != null))
                {
		            if (iStatement != null)
		            {
			            this.WriteStatement(iStatement);
		            }
                }
                else
                {
		            ;
	            }
            }

            void WriteExtendedStatement(IStatement iStatement)
            {
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                this.WriteStatement(iStatement);
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteDelegateDeclMiddle(Object delegateDecl)
            {
	            DelegateDeclMiddleInfo delegateDeclMiddleInfo = (delegateDecl) as DelegateDeclMiddleInfo;

	            this.Write(delegateDeclMiddleInfo.delegateDecl.Name);
	            this.WriteMethodParameterCollection(delegateDeclMiddleInfo.invokeDecl.Parameters);
            }

            void WriteMethodDeclMiddle(IMethodDeclaration methodDeclaration)
            {
	            ITypeDeclaration declaringTypeDecl = (methodDeclaration.DeclaringType as ITypeReference).Resolve();
	            MethodNameExt MNE = new MethodNameExt(methodDeclaration.Name, methodDeclaration.Overrides, declaringTypeDecl.Name);
            #if false
	            //added by writetype
	            if (!MNE.Constructor)
	            {
		            this.Write(" ");	//separate the type
	            }
            #endif
	            this.WriteDeclaration(MNE.Name);
	            WriteMethodParameterCollection(methodDeclaration.Parameters);
            }

            void WriteMethodParameterCollection(IParameterDeclarationCollection parameters)
            {
	            this.Write("(");
	            string separator = "";
	            foreach (IParameterDeclaration parameter in parameters)
	            {
		            this.Write(separator);
		            this.WriteType(parameter.ParameterType, WriteName, parameter.Name, parameter, false);
		            separator = ", ";
	            }
	            this.Write(")");
            }

            public void WriteModule(IModule module)
            {
	            this.Write("// Module");
	            this.Write(" ");
	            this.WriteDeclaration(module.Name);
	            this.WriteLine();
	            if ((this.configuration["ShowCustomAttributes"] == "true") && (module.Attributes.Count != 0))
	            {
		            this.WriteLine();
		            //this.WriteCustomAttributeCollection(this.formatter, module);
		            this.WriteLine();
	            }
	            this.WriteProperty("Version", module.Version.ToString());
	            this.WriteProperty("Location", module.Location);
	            string text1 = Environment.ExpandEnvironmentVariables(module.Location);
	            if (File.Exists(text1))
	            {
		            FileStream stream1 = new FileStream(text1, FileMode.Open, FileAccess.Read);
		            this.WriteProperty("Size", stream1.Length + " Bytes");
	            }
            }

            public void WriteModuleReference(IModuleReference moduleReference)
            {
	            this.Write("// Module Reference");
	            this.Write(" ");
	            this.WriteDeclaration(moduleReference.Name);
	            this.WriteLine();
            }

            public void WriteNamespace(INamespace namespaceDeclaration)
            {
	            if ((this.configuration["ShowNamespaceBody"] == "true"))
	            {
		            string[] names = namespaceDeclaration.Name.Split('.');
		            foreach (string name in names)
		            {
			            this.WriteKeyword("namespace");
			            this.Write(" ");
			            this.WriteDeclaration(name);
			            this.Write(" { ");
		            }

		            this.WriteLine();
		
		            this.WriteIndent();
		            ArrayList list1 = new ArrayList(0);
		            foreach (ITypeDeclaration declaration1 in namespaceDeclaration.Types)
		            {
			            if (Helper.IsVisible(declaration1,this.configuration.Visibility))
			            {
				            list1.Add(declaration1);
			            }
		            }
		
		            if ((this.configuration["SortAlphabetically"] == "true"))
		            {
			            list1.Sort();
		            }
		
		            for (int num1 = 0; num1 < list1.Count; num1++)
		            {
			            if (num1 != 0)
			            {
				            this.WriteLine();
			            }
			            this.WriteTypeDeclaration((ITypeDeclaration) list1[num1]);
		            }
		            this.WriteOutdent();

		            for (int i = names.Length; i > 0; --i)
		            {
			            this.Write("}");
		            }
	            }
	            else
	            {
		            this.WriteKeyword("namespace");
		            this.Write(" ");
		            string name = namespaceDeclaration.Name.Replace(".", ".");
		            this.WriteDeclaration(name);
	            }
            }

            public void WriteResource(IResource resource)
            {
	            this.Write("// ");
	            switch (resource.Visibility)
	            {
	            case ResourceVisibility.Public:
		            {
			            this.WriteKeyword("public");
			            break;
		            }
	            case ResourceVisibility.Private:
		            {
			            this.WriteKeyword("private");
			            break;
		            }
	            }
	            this.Write(" ");
	            this.WriteKeyword("resource");
	            this.Write(" ");
	            this.WriteDeclaration(resource.Name);
	            this.WriteLine();
	            IEmbeddedResource resource1 = (resource) as IEmbeddedResource;
	            if (resource1 != null)
	            {
		            int num1 = resource1.Value.Length;
		            this.WriteProperty("Size", num1.ToString() + " bytes");
	            }
	            IFileResource resource2 = (resource) as IFileResource;
	            if (resource2 != null)
	            {
		            this.WriteProperty("Location", resource2.Location);
	            }
            }

            void WriteBlockStatement(IBlockStatement blockStatement)
            {
	            if (blockStatement.Statements.Count > 0)
	            {
		            this.WriteStatementCollection(blockStatement.Statements,0);
	            }
            }	


            string GetVariableName(IExpression iExpression)
            {
                IVariableDeclarationExpression iVariableDeclarationExpression = iExpression as IVariableDeclarationExpression;
	            if (iVariableDeclarationExpression != null)
	            {
		            return iVariableDeclarationExpression.Variable.Name;
	            }

                IVariableReferenceExpression iVariableReferenceExpression = iExpression as IVariableReferenceExpression;
	            if (iVariableReferenceExpression != null)                    
	            {
		            return iVariableReferenceExpression.Variable.Resolve().Name;
	            }

	            return null;
            }

            void NotAReference(string Name)
            {
	            refOnStack[Name]=Name;
            }

            void WriteStatementCollection(IStatementCollection iStatementCollection, int First)
            {
	            for(int i=First; i<iStatementCollection.Count; i++)
	            {
		            IStatement iStatement = iStatementCollection[i];
		            //Check for Destructor-Dispose
		            //	Declaration of local
		            //	try
		            //	{
		            //		var2 = local
		            //		stuff
		            //	}
		            //	fault
		            //	{
		            //	}
		            //	var2.Dispose()
		            //
		            if (i+2 < iStatementCollection.Count)
		            {
                        IAssignExpression iAssignExpression = iStatement as IAssignExpression;
			            if (iAssignExpression != null)
			            {
                            ITryCatchFinallyStatement iTryCatchFinallyStatement = iStatementCollection[i+1] as ITryCatchFinallyStatement;
				            if (iTryCatchFinallyStatement != null)
				            {
                                IExpressionStatement iExpressionStatement = iStatementCollection[i+2] as IExpressionStatement;
					            if (iExpressionStatement != null)
					            {
                                    IMethodInvokeExpression iMethodInvokeExpression = iExpressionStatement.Expression as IMethodInvokeExpression;
						            if (iMethodInvokeExpression != null)
						            {
                                        IMethodReferenceExpression iMethodReferenceExpression = iMethodInvokeExpression.Method as IMethodReferenceExpression;
							            if (iMethodInvokeExpression.Method != null)
							            {
                                            IMethodReference iMethodReference = iMethodReferenceExpression.Method as IMethodReference;
								            if (iMethodReference != null)
								            {
									            if (iTryCatchFinallyStatement.Try != null)
									            {
										            if (iTryCatchFinallyStatement.Try.Statements.Count > 0)
										            {
                                                        IAssignExpression iAssignExpression2 = iTryCatchFinallyStatement.Try.Statements[0] as IAssignExpression;
											            if (iAssignExpression2 != null)
											            {
												            if (iMethodReference.Name=="Dispose")
												            {
													            string local = GetVariableName(iAssignExpression.Target);
													            string var2 = GetVariableName(iMethodReferenceExpression.Target);
													            string var2A = GetVariableName(iAssignExpression2.Target);
													            string localA = GetVariableName(iAssignExpression2.Expression);
													            if (local==localA && var2==var2A)
													            {
                                                                    IVariableDeclarationExpression iVariableDeclarationExpression = iAssignExpression.Target as IVariableDeclarationExpression;
														            if (iVariableDeclarationExpression != null)
														            {
															            this.Write("{");
															            this.WriteLine();
															            this.WriteIndent();
															            if (!SuppressOutput)
															            {
																            this.NotAReference(iVariableDeclarationExpression.Variable.Name);
															            }
															            this.WriteType<object>(iVariableDeclarationExpression.Variable.VariableType, null, null, null, true);
															            this.Write(" ");
															            this.Write(iVariableDeclarationExpression.Variable.Name);
															            this.Write(";");
															            this.WriteLine();
															            this.WriteBlockStatement(iTryCatchFinallyStatement.Try);
															            this.WriteOutdent();
															            this.Write("}");
															            this.WriteLine();
															            i++;
															            i++;
															            continue;
														            }
													            }
												            }
											            }
										            }
									            }
								            }
							            }
						            }
					            }
				            }
			            }
		            }
		            this.WriteStatement(iStatement);
                }
            }

            public void WriteStatement(IStatement iStatement)
            {
	            try
	            {
                    ILockStatement iLockStatement = iStatement as ILockStatement;
		            if (iLockStatement != null)
		            {
			            WriteLockStatement(iLockStatement);
                        return;
		            }

                    IBreakStatement iBreakStatement = iStatement as IBreakStatement;
		            if (iBreakStatement != null)
		            {
			            WriteBreakStatement(iBreakStatement);
                        return;
		            }

                    IDoStatement iDoStatement = iStatement as IDoStatement;
		            if (iDoStatement != null)
		            {
			            WriteDoStatement(iDoStatement);
                        return;
		            }

                    IGotoStatement iGotoStatement = iStatement as IGotoStatement;
		            if (iGotoStatement != null)
		            {
			            WriteGotoStatement(iGotoStatement);
                        return;
		            }

                    ILabeledStatement iLabeledStatement = iStatement as ILabeledStatement;
		            if (iLabeledStatement != null)
		            {
			            WriteLabeledStatement(iLabeledStatement);
                        return;
		            }

                    IMethodReturnStatement iMethodReturnStatement = iStatement as IMethodReturnStatement;
		            if (iMethodReturnStatement != null)
		            {
			            WriteMethodReturnStatement(iMethodReturnStatement);
                        return;
		            }

                    IForStatement iForStatement = iStatement as IForStatement;
		            if (iForStatement != null)
		            {
			            WriteForStatement(iForStatement);
                        return;
		            }

                    IWhileStatement iWhileStatement = iStatement as IWhileStatement;
		            if (iWhileStatement != null)
		            {
			            WriteWhileStatement(iWhileStatement);
                        return;
		            }

                    IConditionStatement iConditionStatement = iStatement as IConditionStatement;
		            if (iConditionStatement != null)
		            {
			            WriteConditionStatement(iConditionStatement);
			            return;
		            }

                    IBlockStatement iBlockStatement = iStatement as IBlockStatement;
		            if (iBlockStatement != null)
		            {
			            WriteBlockStatement(iBlockStatement);
			            return;
		            }

                    IForEachStatement iForEachStatement = iStatement as IForEachStatement;
		            if (iForEachStatement != null)
		            {
			            WriteForEachStatement(iForEachStatement);
			            return;
		            }

                    IThrowExceptionStatement iThrowExceptionStatement = iStatement as IThrowExceptionStatement;
		            if (iThrowExceptionStatement != null)
		            {
			            WriteThrowExceptionStatement(iThrowExceptionStatement);
			            return;
		            }
                    
                    ITryCatchFinallyStatement iTryCatchFinallyStatement = iStatement as ITryCatchFinallyStatement;
		            if (iTryCatchFinallyStatement != null)
		            {
			            WriteTryCatchFinallyStatement(iTryCatchFinallyStatement);
			            return;
		            }

                    IExpressionStatement iExpressionStatement = iStatement as IExpressionStatement;
		            if (iExpressionStatement != null)
		            {
			            WriteExpressionStatement(iExpressionStatement);
			            return;
		            }

                    IAttachEventStatement iAttachEventStatement = iStatement as IAttachEventStatement;
		            if (iAttachEventStatement != null)
		            {
			            WriteAttachEventStatement(iAttachEventStatement);
			            return;
		            }

                    IRemoveEventStatement iRemoveEventStatement = iStatement as IRemoveEventStatement;
		            if (iRemoveEventStatement != null)
		            {
			            WriteRemoveEventStatement(iRemoveEventStatement);
			            return;
		            }

                    ISwitchStatement iSwitchStatement = iStatement as ISwitchStatement;
		            if (iSwitchStatement != null)
		            {
			            WriteSwitchStatement(iSwitchStatement);
			            return;
		            }

                    IContinueStatement iContinueStatement = iStatement as IContinueStatement;
		            if (iContinueStatement != null)
		            {
			            WriteContinueStatement(iContinueStatement);
			            return;
		            }

                    IUsingStatement iUsingStatement = iStatement as IUsingStatement;
                    if (iUsingStatement != null)
		            {
			            WriteUsingStatement(iUsingStatement);
			            return;
		            }

		            try {
			            this.Write(iStatement.ToString());
		            }
		            catch(Exception e)
		            {
			            this.Write(e.ToString());
			            this.Write(";");
		            }
	            }
	            finally
	            {
		            SkipWriteLine = false;
	            }
            }

            void WriteExpressionStatement(IExpressionStatement expressionStatement)
            {
	            WriteExpression(expressionStatement.Expression);
	            if (!SkipWriteLine)
	            {
		            this.Write(";");
		            this.WriteLine();
	            }
            }

            public void WriteTypeDeclaration(ITypeDeclaration typeDeclaration)
            {
	            if ((this.configuration["ShowCustomAttributes"] == "true") && (typeDeclaration.Attributes.Count != 0))
	            {
		            this.WriteCustomAttributeCollection(typeDeclaration, null);
		            this.WriteLine();
	            }
	            if (Helper.IsDelegate(typeDeclaration))
	            {
		            WriteDelegateDeclaration(typeDeclaration);
	            }
	            else if (Helper.IsEnumeration(typeDeclaration))
	            {
		            WriteEnumDeclaration(typeDeclaration);
	            }
	            else
	            {
		            WriteClassDeclaration(typeDeclaration);
	            }
            }

			class DelegateDeclMiddleInfo
			{
			    internal DelegateDeclMiddleInfo(ITypeDeclaration delegateDecl_, IMethodDeclaration invokeDecl_)
				{
					delegateDecl = delegateDecl_;
					invokeDecl = invokeDecl_;
				}

				internal ITypeDeclaration delegateDecl;
                internal IMethodDeclaration invokeDecl;
			};

            void WriteDelegateDeclaration(ITypeDeclaration typeDeclaration)
            {
                bool f = false;
	            WriteGenericArguments(typeDeclaration.GenericArguments, ref f);
	            WriteTypeVisibilitySpecifier(typeDeclaration.Visibility);
	            this.WriteKeyword("delegate");
	            this.Write(" ");
	            IMethodDeclaration invokeDecl = Helper.GetMethod(typeDeclaration,"Invoke");
	            this.WriteType(invokeDecl.ReturnType.Type,
			            this.WriteDelegateDeclMiddle,
			            new DelegateDeclMiddleInfo(typeDeclaration, invokeDecl), null, false);
	            this.Write(";");
	            this.WriteLine();
            }

            void WriteEnumDeclaration(ITypeDeclaration typeDeclaration)
            {
	            WriteTypeVisibilitySpecifier(typeDeclaration.Visibility);

	            this.WriteKeyword("value");
	            this.Write(" ");
	            this.WriteKeyword("enum");
	            this.Write(" ");
	            this.Write(typeDeclaration.Name);

	            if ((this.configuration["ShowTypeDeclarationBody"] == "true"))
	            {
		            ArrayList fieldList = new ArrayList();
		            ICollection iCollection = 
			            Helper.GetFields(typeDeclaration,this.configuration.Visibility);
		            foreach (IFieldDeclaration fieldDeclaration in iCollection)
		            {
			            if (fieldDeclaration.SpecialName && fieldDeclaration.Name == "value__")
			            {
				            IType underlyingType = fieldDeclaration.FieldType;
				
				            // Print the underlying type unless it's the default of 'int'
				            if (!Type(underlyingType, "System", "Int32"))
				            {
					            ITypeReference tagType = (underlyingType) as ITypeReference;

					            this.Write(" : ");
					            this.WriteTypeReference(tagType);
				            }
			            }
			            else
			            {
				            fieldList.Add(fieldDeclaration);
			            }
		            }

		            this.WriteLine();
		            this.Write("{");
		            this.WriteLine();
		            this.WriteIndent();

		            if ((this.configuration["SortAlphabetically"] == "true"))
		            {
			            fieldList.Sort();
		            }

		            this.WriteComment("// Enumerators");
		            foreach (IFieldDeclaration fieldDeclaration in fieldList)
		            {
			            this.WriteLine();
			            this.WriteDeclaration(fieldDeclaration.Name);
			            this.Write(" = ");
			            WriteExpression(fieldDeclaration.Initializer);
			            this.Write(",");
		            }

		            this.WriteOutdent();
		            this.WriteLine();
		            this.Write("};");
		            this.WriteLine();
	            }
            }

            void WriteGenericArguments(ITypeCollection iTypeCollection, ref bool needNewline)
            {
                if (needNewline)
                {
                    this.WriteLine();
                    needNewline = false;
                }

	            if (iTypeCollection != null)
	            {
		            if (iTypeCollection.Count > 0)
		            {
			            this.WriteKeyword("generic");
			            this.Write(" ");
			            string separator = "<";
			            foreach (IType iType in iTypeCollection)
			            {
				            this.Write(separator);
				            this.WriteKeyword("typename");
				            this.Write(" ");
				            this.WriteType<object>(iType,null,null,null, false);
				            separator = ", ";
			            }
			            this.Write(">");
			            this.WriteLine();
		            }
	            }
            }

            void WriteClassDeclaration(ITypeDeclaration typeDeclaration)
            {

	            //	this.WriteCustomAttributeCollection(typeDeclaration,null);
	            WriteTypeVisibilitySpecifier(typeDeclaration.Visibility);

	            bool isValueClass = false;
	            bool isInterface = false;
                bool unused = false;
	            WriteGenericArguments(typeDeclaration.GenericArguments, ref unused);

                if (Helper.IsValueType(typeDeclaration))
	            {
		            this.WriteKeyword("value");
		            this.Write(" ");
		            this.WriteKeyword("class");
		            isValueClass = true;
	            }
	            else if (typeDeclaration.Interface)
	            {
		            this.WriteKeyword("interface");
		            this.Write(" ");
		            this.WriteKeyword("class");
		            isInterface = true;
	            }
	            else
	            {
		            // Defaults to ref class.  Note that typeInformation.Object appears to mean System.Object which is itself a ref class.
		            this.WriteKeyword("ref");
		            this.Write(" ");
		            this.WriteKeyword("class");
	            }

	            this.Write(" ");
	            this.Write(typeDeclaration.Name);

	            if (typeDeclaration.Abstract && !isInterface)
	            {
		            this.Write(" ");
		            this.WriteKeyword("abstract");
	            }

	            if (typeDeclaration.Sealed && !isValueClass)
	            {
		            this.Write(" ");
		            this.WriteKeyword("sealed");
	            }

	            string separator = " : ";

	            if (typeDeclaration.BaseType != null)
	            {
                    if (!Helper.IsObject(typeDeclaration.BaseType) && !Helper.IsValueType(typeDeclaration.BaseType))
		            {
			            this.Write(separator);
			            this.WriteKeyword("public");
			            this.Write(" ");
			            WriteTypeReference(typeDeclaration.BaseType);
			            separator = ", ";
		            }
	            }

	            foreach (ITypeReference typeReference in typeDeclaration.Interfaces)
	            {
		            // TODO: handle IDisposable specially

		            this.Write(separator);
		            this.WriteKeyword("public");
		            this.Write(" ");
		            WriteTypeReference(typeReference);
		            separator = ", ";
	            }

	            this.WriteLine();
	            this.Write("{");
	            this.WriteLine();
	            this.WriteIndent();

	            if ((this.configuration["ShowTypeDeclarationBody"] == "true"))
	            {
		            ICollection nestedTypes = Helper.GetNestedTypes(typeDeclaration,this.configuration.Visibility);
		            if (nestedTypes.Count > 0)
		            {
			            this.WriteComment("// Nested types");
			            this.WriteLine();
			            foreach (ITypeDeclaration nestedTypeDeclaration in nestedTypes)
			            {
				            this.WriteTypeDeclaration(nestedTypeDeclaration);
				            this.WriteLine();
			            }
		            }

		            ICollection methods = Helper.GetMethods(typeDeclaration,this.configuration.Visibility);
		            if (methods.Count > 0)
		            {
			            this.WriteComment("// Methods");
			            this.WriteLine();
			            foreach (IMethodDeclaration methodDecl in methods)
			            {
				            this.WriteMethodDeclaration(methodDecl);
				            this.WriteLine();
			            }
		            }

		            ICollection fields = Helper.GetFields(typeDeclaration,this.configuration.Visibility);
		            if (fields.Count > 0)
		            {
			            this.WriteComment("// Fields");
			            this.WriteLine();
			            foreach (IFieldDeclaration fieldDecl in fields)
			            {
				            this.WriteFieldDeclaration(fieldDecl);
				            this.WriteLine();
			            }
		            }
		            ICollection properties = Helper.GetProperties(typeDeclaration,this.configuration.Visibility);
		            if (properties.Count > 0)
		            {
			            this.WriteComment("// Properties");
			            this.WriteLine();
			            foreach (IPropertyDeclaration iPropertyDeclaration in properties)
			            {
				            this.WritePropertyDeclaration(iPropertyDeclaration);
				            this.WriteLine();
			            }
		            }
		            ICollection events = Helper.GetEvents(typeDeclaration,this.configuration.Visibility);
		            if (events.Count > 0)
		            {
			            this.WriteComment("// Events");
			            this.WriteLine();
			            foreach (IEventDeclaration iEventDeclaration in events)
			            {
				            this.WriteEventDeclaration(iEventDeclaration);
				            this.WriteLine();
			            }
		            }
	            }
	            this.WriteOutdent();
	            this.Write("};");
	            this.WriteLine();
            }

            string GetCustomAttributeName(ICustomAttribute iCustomAttribute)
            {
	            ITypeReference iTypeReference = (iCustomAttribute.Constructor.DeclaringType) as ITypeReference;
	            string name = GetNameFromTypeReference(iTypeReference);
	            if (name.EndsWith("Attribute"))
	            {
		            name = name.Substring(0, name.Length - 9);
	            }
	            return name;
            }

            void WriteCustomAttribute(ICustomAttribute iCustomAttribute)
            {
	            string name = GetCustomAttributeName(iCustomAttribute);
	            this.WriteReference(name, ""/*this.GetMethodReferenceDescription(iCustomAttribute.Constructor)*/, iCustomAttribute.Constructor);

	            IExpressionCollection arguments= iCustomAttribute.Arguments;
	            if (arguments.Count != 0)
	            {
		            string separator = "(";
		            foreach (IExpression expr in arguments)
		            {
			            this.Write(separator);
			            this.WriteExpression(expr);
			            separator = ", ";
		            }

		            this.Write(")");
	            }
            }

            void WriteCustomAttributeCollection(ICustomAttributeProvider provider, IType iType)
            {
	            if (!(this.configuration["ShowCustomAttributes"] == "true"))
	            {
		            return;
	            }
	            if (provider == null)
	            {
		            return;
	            }
	            if (provider.Attributes.Count == 0)
	            {
		            return;
	            }
	            ArrayList arrayList = new ArrayList();
	            arrayList.AddRange(provider.Attributes);
	            bool ParamArrayFlag = false;
	            string text1 = null;
	            if (provider is IAssembly)
	            {
		            text1 = "assembly:";
	            }
	            if (provider is IModule)
	            {
		            text1 = "module:";
	            }
	            if (provider is IMethodReturnType)
	            {
		            text1 = "returnvalue:";
	            }
	            //
	            // we don't need to generate Attributes which are created by the compiler.
	            //
	            for(int index=arrayList.Count-1; index>=0; index--)
	            {
                    ICustomAttribute iCustomAttribute = arrayList[index] as ICustomAttribute;
		            if (iCustomAttribute != null)
		            {
			            string name = GetCustomAttributeName(iCustomAttribute);
			            if (name == "System.ParamArray")
			            {
				            ParamArrayFlag = true;
				            arrayList.RemoveAt(index);
				            continue;
			            }

			            // See if MarshalAs is being generated automatically for bool or wchar_t
			            if (name == "System.Runtime.InteropServices.MarshalAs")
			            {
                            IExpressionCollection iExpressionCollection = iCustomAttribute.Arguments;
				            if (iExpressionCollection != null)
				            {
                                IFieldReferenceExpression iFieldReferenceExpression = iExpressionCollection[0] as IFieldReferenceExpression;
					            if (iFieldReferenceExpression != null)
					            {
                                    ITypeReferenceExpression iTypeReferenceExpression = iFieldReferenceExpression.Target as ITypeReferenceExpression;
						            if (iTypeReferenceExpression != null)
						            {
							            if (iTypeReferenceExpression.Type.Name == "UnmanagedType")
							            {
								            IFieldReference iFieldReference = iFieldReferenceExpression.Field;
								            if (iFieldReference.Name == "U1")
								            {
									            if (Type(iType, "System", "Boolean"))
									            {
										            arrayList.RemoveAt(index);
										            continue;
									            }
								            }
								            else if (iFieldReference.Name == "U2")
								            {
									            if (Type(iType, "System", "Char"))
									            {
										            arrayList.RemoveAt(index);
										            continue;
									            }
								            }
							            }
						            }	
					            }
				            }
			            }
		            }
	            }
	            if (ParamArrayFlag)
	            {
		            this.Write("...");
	            }
	            if (text1 != null)
	            {
		            foreach(ICustomAttribute iCustomAttribute in arrayList)
		            {
			            this.Write("[");
			            this.WriteKeyword(text1);
			            this.Write(" ");
			            this.WriteCustomAttribute(iCustomAttribute);
			            this.Write("]");
			            this.WriteLine();
		            }
	            }
	            else
	            {
		            if (arrayList.Count > 0)
		            {
			            string separator = "[";
			            foreach(ICustomAttribute iCustomAttribute in arrayList)
			            {
				            this.Write(separator);
				            this.WriteCustomAttribute(iCustomAttribute);
				            separator=",";
			            }
			            this.Write("]");
		            }
	            }
            }

            void WriteFieldVisibilitySpecifier(FieldVisibility visibility)
            {
	            switch (visibility)
	            {
	            case FieldVisibility.Assembly:
		            this.WriteKeyword("internal");
		            break;

	            case FieldVisibility.Family:
		            this.WriteKeyword("protected");
		            break;

	            case FieldVisibility.FamilyAndAssembly:
		            this.WriteKeyword("private protected");
		            break;

	            case FieldVisibility.FamilyOrAssembly:
		            this.WriteKeyword("public protected");
		            break;

	            case FieldVisibility.Private:
		            this.WriteKeyword("private");
		            break;

	            case FieldVisibility.PrivateScope:
		            this.WriteKeyword("?PrivateScope?");
		            break;

	            case FieldVisibility.Public:
		            this.WriteKeyword("public");
		            break;
	            }
	            this.Write(":");
	            this.WriteLine();
            }

            void WriteMethodVisibilitySpecifier(MethodVisibility visibility)
            {
	            switch (visibility)
	            {
	            case MethodVisibility.Assembly:
		            this.WriteKeyword("internal");
		            break;

	            case MethodVisibility.Family:
		            this.WriteKeyword("protected");
		            break;

	            case MethodVisibility.FamilyAndAssembly:
		            this.WriteKeyword("private protected");
		            break;

	            case MethodVisibility.FamilyOrAssembly:
		            this.WriteKeyword("public protected");
		            break;

	            case MethodVisibility.Private:
		            this.WriteKeyword("private");
		            break;

	            case MethodVisibility.PrivateScope:
		            this.WriteKeyword("?PrivateScope?");
		            break;

	            case MethodVisibility.Public:
		            this.WriteKeyword("public");
		            break;
	            }

	            this.Write(":");
            }

            void WriteTypeVisibilitySpecifier(TypeVisibility visibility)
            {
	            bool nested = true;

	            switch (visibility)
	            {
	            case TypeVisibility.Public:
		            nested = false;
		            this.WriteKeyword("public");
		            break;

	            case TypeVisibility.NestedPublic:
		            this.WriteKeyword("public");
		            break;

	            case TypeVisibility.Private:
		            nested = false;
		            this.WriteKeyword("private");
		            break;

	            case TypeVisibility.NestedPrivate:
		            this.WriteKeyword("private");
		            break;

	            case TypeVisibility.NestedAssembly:
		            this.WriteKeyword("internal");
		            break;

	            case TypeVisibility.NestedFamily:
		            this.WriteKeyword("protected");
		            break;

	            case TypeVisibility.NestedFamilyAndAssembly:
		            this.WriteKeyword("protected");
		            this.Write(" ");
		            this.WriteKeyword("private");
		            break;

	            case TypeVisibility.NestedFamilyOrAssembly:
		            this.WriteKeyword("protected");
		            this.Write(" ");
		            this.WriteKeyword("public");
		            break;
	            }
	            if (nested)
	            {
		            this.Write(":");
		            this.WriteLine();
	            }
	            else
	            {
		            this.Write(" ");
	            }
            }

            bool Type(IType type, string typeNamespace, string typeName)
            {
                ITypeReference iTypeReference = type as ITypeReference;
	            if (iTypeReference != null)
	            {
		            return iTypeReference.Namespace == typeNamespace && iTypeReference.Name == typeName;
	            }
	            return false;
            }

            void WriteModifiedType(ITypeReference modifier, IType type, bool required, bool fRefOnStack)
            {
	            if (this.Type(modifier, "System.Runtime.CompilerServices", "Volatile"))
	            {
		            this.WriteKeyword("volatile");
		            this.Write(" ");
                    this.WriteType<object>(type, null, null, null, fRefOnStack);
	            }
	            else if (this.Type(modifier, "System.Runtime.CompilerServices", "IsConst"))
	            {
		            // If we are doing the special Ref class on Stack construction, we need to eat the constant modifier.
		            if (!fRefOnStack)
		            {
			            this.WriteKeyword("const");
			            this.Write(" ");
		            }
                    this.WriteType<object>(type, null, null, null, fRefOnStack);
	            }
	            else if (this.Type(modifier, "System.Runtime.CompilerServices", "long"))
	            {
		            this.WriteKeyword("long");
	            }
	            // else CxxReference, CxxPointer, NoSignSpecified, NeedsCopyConstructor, IsByValueModifier, IsImplicitlyDereferencedModifier, Boxed
	            else
	            {
                    this.WriteType<object>(type, null, null, null, fRefOnStack);
		            this.Write(" ");
		            this.WriteKeyword(required ? "modreq" : "modopt");
		            this.Write("(");
		            this.WriteType<object>(modifier, null, null, null, false);
		            this.Write(")");
	            }
            }


            void WriteTryCatchFinallyStatement(ITryCatchFinallyStatement iStatement)
            {
	            int LocalSkipTryCount = SkipTryCount;
	            SkipTryCount = 0;
	            if (iStatement.Try != null)
	            {
		            bool Interesting = false;
		            foreach (ICatchClause clause1 in iStatement.CatchClauses)
		            {
			            if (clause1.Body.Statements.Count > 0)
			            {
				            Interesting = true;
				            break;
			            }
		            }
		            if (!Interesting && iStatement.Finally.Statements.Count > 0)
		            {
			            Interesting = true;
		            }
		            if (!Interesting)
		            {
			            WriteBlockStatement(iStatement.Try);		//not an interesting try/catch (manufactured?), just print inside of try.
			            if (LocalSkipTryCount > 0)						//need to complete function try block
			            {
				            this.WriteOutdent();
				            this.Write("}");
				            this.WriteLine();
			            }
			            return;
		            }
	            }
	            if (LocalSkipTryCount == 0)
	            {
		            this.WriteKeyword("try");
		            this.WriteLine();
		            this.Write("{");
		            this.WriteLine();
		            this.WriteIndent();
	            }
	            if (iStatement.Try != null)
	            {
		            IBlockStatement iBlockStatement = iStatement.Try;
		            IStatementCollection iStatementCollection = iBlockStatement.Statements;
		            if (iStatementCollection.Count > 0)
		            {
			            for(int i=0; i<LocalSkipTryCount; i++)
			            {
				            IStatement iStatement2 = iStatementCollection[i];
				            IAssignExpression  iAssignExpression = iStatement2 as IAssignExpression;
				            if (iAssignExpression != null)
				            {
					            IFieldReferenceExpression iFieldReferenceExpression = (iAssignExpression.Target) as IFieldReferenceExpression;
					            if (iFieldReferenceExpression != null)
					            {
						            continue;	//done
					            }
				            }
				            else
				            {
					            IExpressionStatement iExpressionStatement = iStatement2 as IExpressionStatement;
					            if (iExpressionStatement != null)
					            {
						            IMethodInvokeExpression iMethodInvokeExpression = (iExpressionStatement.Expression) as IMethodInvokeExpression;
						            if (iMethodInvokeExpression != null)
						            {
							            IMethodReferenceExpression iMethodReferenceExpression = (iMethodInvokeExpression.Method) as IMethodReferenceExpression;
							            if (iMethodReferenceExpression != null)
							            {
								            IBaseReferenceExpression iBaseReferenceExpression = (iMethodReferenceExpression.Target) as IBaseReferenceExpression;
								            if (iBaseReferenceExpression != null)
								            {
									            continue;	//done
								            }
							            }
						            }
					            }
				            }
				            this.WriteStatement(iStatement2);
			            }
			            this.WriteStatementCollection(iStatementCollection, LocalSkipTryCount);
		            }
	            }
	            this.WriteOutdent();
	            this.Write("}");
	            this.WriteLine();
	            foreach (ICatchClause clause1 in iStatement.CatchClauses)
	            {
		            this.WriteKeyword("catch");
		            ITypeReference reference1 = (ITypeReference ) clause1.Variable.VariableType;
		            bool flag1 = clause1.Variable.Name.Length == 0;
		            bool flag2 = Helper.IsObject(reference1);
		            if (!flag1 || !flag2)
		            {
			            this.Write(" ");
			            this.Write("(");
			            this.WriteType(clause1.Variable.VariableType, 
				            WriteName, clause1.Variable.Name, null, false);
			            this.Write(")");
		            }
		            if (clause1.Condition != null)
		            {
			            this.Write(" ");
			            this.WriteKeyword("when");
			            this.Write(" ");
			            this.Write("(");
			            this.WriteExpression(clause1.Condition);
			            this.Write(")");
		            }
		            this.WriteLine();
		            this.Write("{");
		            this.WriteLine();
		            this.WriteIndent();
		            if (clause1.Body != null)
		            {
			            IBlockStatement iBlockStatement = clause1.Body;
			            IStatementCollection iStatementCollection = iBlockStatement.Statements;
			            for(int i=0; i<iStatementCollection.Count; i++)
			            {
				            IStatement iStatement2 = iStatementCollection[i];
				            //check for last throw at end of catch clause in destructor/constructor.
				            if (SomeConstructor && (i+1 >= iStatementCollection.Count))
				            {
					            if (iStatement2 is IThrowExceptionStatement)
					            {
						            continue;	//eat it
					            }
				            }
				            this.WriteStatement(iStatement2);
			            }	
		            }
		            this.WriteOutdent();
		            this.Write("}");
		            this.WriteLine();
	            }
	            if ((iStatement.Fault != null) && (iStatement.Fault.Statements.Count > 0))
	            {
		            this.WriteKeyword("fault");
		            this.WriteLine();
		            this.Write("{");
		            this.WriteLine();
		            this.WriteIndent();
		            if (iStatement.Fault != null)
		            {
			            this.WriteStatement(iStatement.Fault);
		            }
		            this.WriteOutdent();
		            this.Write("}");
		            this.WriteLine();
	            }
	            if ((iStatement.Finally != null) && (iStatement.Finally.Statements.Count > 0))
	            {
		            this.WriteKeyword("finally");
		            this.WriteLine();
		            this.Write("{");
		            this.WriteLine();
		            this.WriteIndent();
		            if (iStatement.Finally != null)
		            {
			            this.WriteStatement(iStatement.Finally);
		            }
		            this.WriteOutdent();
		            this.Write("}");
		            this.WriteLine();
	            }
            }

            void WriteMethodInvokeExpression(IMethodInvokeExpression iMethodInvokeExpression)
            {
            #if false
	            if (IMethodReferenceExpression iMethodReferenceExpression = (iMethodInvokeExpression.Method)) as IMethodReferenceExpression 
	            {
		            if (IMethodReference iMethodReference = (iMethodReferenceExpression.Method)) as IMethodReference 
		            {
			            if (iMethodReference.Name == "Dispose")
			            {
				            SkipWriteLine = true;
				            if (!SuppressOutput)
				            {
					            if (InDisposePattern>0)
					            {
						            if (--InDisposePattern == 0)
						            {
							            this.WriteOutdent();
							            this.Write("}");
						            }
					            }
					            return;	//ignore Dispose() calls for now. We're decompiling (not quite accurate)
				            }
				            else
				            {
					            DisposeMap = -1;
					            DisposeMapFlag = true;
				            }
			            }
		            }
	            }
            #endif
	            this.WriteExpression(iMethodInvokeExpression.Method);
            #if false
	            if (DisposeMapFlag && DisposeMappings!=null)
	            {
		            DisposeMapFlag = false;
		            if (DisposeMap>=0)
		            {
			            DisposeMappings[DisposeMap]=true;
		            }
	            }
            #endif
	            this.Write("(");
	            this.WriteExpressionCollection(iMethodInvokeExpression.Arguments);
	            this.Write(")");
            }
            void WriteCastExpression(ICastExpression iCastExpression)
            {
	            this.Write("((");
                this.WriteType<object>(iCastExpression.TargetType, null, null, null, false);
	            this.Write(") ");
	            this.WriteExpression(iCastExpression.Expression);
	            this.Write(")");
            }

            void WriteObjectCreateExpression(IObjectCreateExpression iObjectCreateExpression)
            {
            #if false	//deanwi broken
	            if (!Helper.IsValueType(iObjectCreateExpression.Constructor.DeclaringType))
	            {
		            this.Write("new ");
	            }
            #endif
                ITypeReference iTypeReference = iObjectCreateExpression.Constructor.DeclaringType as ITypeReference;
	            if (iTypeReference != null)
	            {
                    if (!Helper.IsValueType(iTypeReference))
		            {	
			            this.Write("new ");
		            }
		            this.WriteTypeReference(iTypeReference);
	            }
	            this.Write("(");
	            this.WriteExpressionCollection(iObjectCreateExpression.Arguments);
	            this.Write(") ");
            }

            void WriteTypeCollection(ITypeCollection iTypeCollection)
            {
	            string separator = "";
	            foreach (IType iType in iTypeCollection)
	            {
		            this.Write(separator);
                    this.WriteType<object>(iType, null, null, null, false);
		            separator = ", ";
	            }
            }

            void WriteTypeReferenceExpression(ITypeReferenceExpression iTypeReferenceExpression)
            {
	            if (iTypeReferenceExpression.Type.Name == "UnmanagedType")
	            {
		            return;
	            }
	            if (iTypeReferenceExpression.Type.Name == "<Module>")
	            {
		            return;
	            }

	            this.WriteTypeReference(iTypeReferenceExpression.Type);
            }

            void WriteBaseReferenceExpression(IBaseReferenceExpression iBaseReferenceExpression)
            {
	            this.WriteTypeReference(this.baseType);
            }

            bool BlankStatement(IStatement iStatement)
            {
	            if (iStatement == null)
	            {
		            return true;
	            }
                IBlockStatement iBlockStatement = iStatement as IBlockStatement;
	            if (iBlockStatement != null)
	            {
                    IStatementCollection iStatementCollection = iBlockStatement.Statements as IStatementCollection;
		            if (iStatementCollection != null)
		            {
			            return (iStatementCollection.Count == 0);
		            }
	            }
	            return false;
            }

            void WriteConditionStatement(IConditionStatement iConditionStatement)
            {
                int block1 = Block;
                Block = NextBlock++;
                try
                {
	                this.WriteKeyword("if");
	                this.Write(" ");
	                this.Write("(");
	                this.WriteExpression(iConditionStatement.Condition);
	                this.Write(")");
	                this.WriteLine();

                    int block2 = Block;
                    Block = NextBlock++;
                    try
                    {
	                    this.WriteExtendedStatement(iConditionStatement.Then);
	                    if (!BlankStatement(iConditionStatement.Else))
	                    {
                            int block3 = Block;
                            Block = NextBlock++;
                            try
                            {
                                this.WriteKeyword("else");
                                this.WriteLine();
                                this.WriteExtendedStatement(iConditionStatement.Else);
                            }
                            finally
                            {
                                Block = block3;
                            }
	                    }
                    }
                    finally
                    {
                        Block = block2;
                    }
                }
                finally
                {
                    Block = block1;
                }
            }

            void WriteBinaryExpression(IBinaryExpression iBinaryExpression)
            {
	            this.Write("(");
	            this.WriteExpression(iBinaryExpression.Left);
	            this.Write(" ");
	            this.WriteBinaryOperator(iBinaryExpression.Operator);
	            this.Write(" ");
	            this.WriteExpression(iBinaryExpression.Right);
	            this.Write(")");
            }

            void WriteBinaryOperator(BinaryOperator binaryOperator)
            {
	            switch (binaryOperator)
	            {
	            case BinaryOperator.Add:
		            this.Write("+");
                    break;
                case BinaryOperator.Subtract:
		            this.Write("-");
                    break;
                case BinaryOperator.Multiply:
		            this.Write("*");
                    break;
                case BinaryOperator.Divide:
		            this.Write("/");
                    break;
                case BinaryOperator.Modulus:
		            this.Write("%");
                    break;
                case BinaryOperator.ShiftLeft:
		            this.Write("<<");
                    break;
                case BinaryOperator.ShiftRight:
		            this.Write(">>");
                    break;
                case BinaryOperator.IdentityEquality:
		            this.Write("==");
                    break;
                case BinaryOperator.IdentityInequality:
		            this.Write("!=");
                    break;
                case BinaryOperator.ValueEquality:
		            this.Write("==");
                    break;
                case BinaryOperator.ValueInequality:
		            this.Write("!=");
                    break;
                case BinaryOperator.BitwiseAnd:
		            this.Write("&");
                    break;
                case BinaryOperator.BitwiseOr:
		            this.Write("|");
                    break;
                case BinaryOperator.BitwiseExclusiveOr:
		            this.Write("");
                    break;
                case BinaryOperator.BooleanAnd:
		            this.Write("&&");
                    break;
                case BinaryOperator.BooleanOr:
		            this.Write("||");
                    break;
                case BinaryOperator.GreaterThan:
		            this.Write(">");
                    break;
                case BinaryOperator.GreaterThanOrEqual:
		            this.Write(">=");
                    break;
                case BinaryOperator.LessThan:
		            this.Write("<");
                    break;
                case BinaryOperator.LessThanOrEqual:
		            this.Write("<=");
                    break;
	            default:
		            throw new NotSupportedException(binaryOperator.ToString());
	            }
            }

            void WriteUnaryExpression(IUnaryExpression iUnaryExpression)
            {
	            switch (iUnaryExpression.Operator)
                {
		            case UnaryOperator.Negate:
		            {
			            this.Write("-");
			            this.WriteExpression(iUnaryExpression.Expression);
			            return;
		            }
		            case UnaryOperator.BooleanNot:
		            {
			            this.Write("!");
			            this.WriteExpression(iUnaryExpression.Expression);
			            return;
		            }
		            case UnaryOperator.BitwiseNot:
		            {
			            this.Write("~");
			            this.WriteExpression(iUnaryExpression.Expression);
			            return;
		            }
		            case UnaryOperator.PreIncrement:
		            {
			            this.Write("++");
			            this.WriteExpression(iUnaryExpression.Expression);
			            return;
		            }
		            case UnaryOperator.PreDecrement:
		            {
			            this.Write("--");
			            this.WriteExpression(iUnaryExpression.Expression);
			            return;
		            }
		            case UnaryOperator.PostIncrement:
		            {
			            this.WriteExpression(iUnaryExpression.Expression);
			            this.Write("++");
			            return;
		            }
		            case UnaryOperator.PostDecrement:
		            {
			            this.WriteExpression(iUnaryExpression.Expression);
			            this.Write("--");
			            return;
		            }
                }
	            throw new NotSupportedException(iUnaryExpression.Operator.ToString());
            }

            void WriteMethodReturnStatement(IMethodReturnStatement iMethodReturnStatement)
            {
	            this.WriteKeyword("return");
	            if (iMethodReturnStatement.Expression != null)
	            {
		            this.Write(" ");
		            this.WriteExpression(iMethodReturnStatement.Expression);
                }
	            this.Write(";");
	            this.WriteLine();
            }

            int GatherExtraTemporaries(IStatementCollection iStatementCollection)
            {
	            if (ExtraTemporaries == null)
	            {
		            for(int pass=0; pass<2; pass++)
		            {
			            int i;
			            for(i=0; i<iStatementCollection.Count; i++)
			            {
				            IStatement iStatement = iStatementCollection[i];
                            IAssignExpression iAssignExpression = iStatement as IAssignExpression;
				            if (iAssignExpression != null)
				            {
                                ILiteralExpression iLiteralExpression = iAssignExpression.Expression as ILiteralExpression;
					            if (iLiteralExpression != null)
					            {
						            if (iLiteralExpression.Value == null)
						            {
                                        IVariableDeclarationExpression iVariableDeclarationExpression = iAssignExpression.Target as IVariableDeclarationExpression;
							            if (iVariableDeclarationExpression != null)
							            {
								            if (pass==1)
								            {
									            ExtraTemporaries[i]=iVariableDeclarationExpression.Variable.Name;
								            }
								            continue;	//allow null initializations, tentatively
							            }
						            }
					            }
				            }
				            break;
			            }
			            if (pass==0)
			            {
				            ExtraTemporaries = new string[SkipNullptrCount=i];
                            ExtraMappings = new string[SkipNullptrCount];
                            EssentialTemporaries = new bool[SkipNullptrCount];
                            TemporaryBlocks = new int[SkipNullptrCount];
			            }
		            }
	            }
                return SkipNullptrCount;
            }

            void WriteAssignExpression(IAssignExpression iAssignExpression)
            {
	            IVariableDeclarationExpression iVariableDeclarationExpression=(iAssignExpression.Target) as IVariableDeclarationExpression;
	            IVariableReferenceExpression iVariableReferenceExpressionL=(iAssignExpression.Target) as IVariableReferenceExpression;
	            IVariableReferenceExpression iVariableReferenceExpressionR=(iAssignExpression.Expression) as IVariableReferenceExpression;
	            if (ExtraTemporaries != null)
	            {
		            // delete ExtraTemporaries declaration
		            for(int i=0; i<ExtraTemporaries.Length; i++)
		            {
			            if (EssentialTemporaries[i])
			            {
				            continue;
			            }
			            if (iVariableDeclarationExpression != null)
			            {
				            if (iVariableDeclarationExpression.Variable.Name == ExtraTemporaries[i])
				            {
					            return;
				            }
			            }
		            }
		            // handle ExtraTemporaries being mapped
		            if (iVariableReferenceExpressionL != null && iVariableReferenceExpressionR != null)
		            {
			            int left=-1, right=-1;
			            for(int i=0; i<ExtraTemporaries.Length; i++)
			            {
				            if (iVariableReferenceExpressionL.Variable.Resolve().Name == ExtraTemporaries[i])
				            {
					            left = i;
					            VerifyCorrectBlock(i);
				            }
				            if (iVariableReferenceExpressionR.Variable.Resolve().Name == ExtraTemporaries[i])
				            {
					            right = i;
					            VerifyCorrectBlock(i);
				            }
			            }
			            // 7jan06:dcw
			            // I'm not sure if there are bugs in this code. The original intent
			            // was that if you get here, you're definitely remapping, and the 
			            // return statement at the end caught all. It looks like I carved out
			            // an exception when the right side had never been mapped, but i'm not
			            // sure what case that was, and then I returned on that case anyway.
			            // So, I have cut this back. I also only remap temporaries when
			            // the first character of the names is identical. That way we can show
			            // conversions from int to object with boxing, which would get eaten otherwise.
			            if (left>=0 && !EssentialTemporaries[left])
			            {
				            string s;
				            if (right>=0 && !EssentialTemporaries[right])
				            {
					            if (ExtraMappings[right]==null)
					            {
						            EssentialTemporaries[right]=true;	//how do we get here?
						            s = null;
					            }
					            else
					            {
						            s = ExtraMappings[right];
					            }
				            }
				            else
				            {
					            s = iVariableReferenceExpressionR.Variable.Resolve().Name;
				            }
				            if (s != null)
				            {
					            if (iVariableReferenceExpressionL.Variable.Resolve().Name[0] == s[0])
					            {
						            ExtraMappings[left] = s;
						            return;
					            }
				            }
			            }
		            }
	            }
	            WriteExpression(iAssignExpression.Target);
	            this.Write(" = ");
	            WriteExpression(iAssignExpression.Expression);
            }

            bool VerifyCorrectBlock(int i)
            {
	            if (SuppressOutput)								//first pass
	            {
		            if (TemporaryBlocks[i] == 0)
		            {
			            TemporaryBlocks[i] = Block;
		            }
		            else if (TemporaryBlocks[i] != Block)			//did we encounter this elsewhere?
		            {
			            EssentialTemporaries[i] = true;
			            return false;
		            }
	            }
	            return true;
            }

            string  MapTemporaryName(string s)
            {
	            if (ExtraTemporaries != null)
	            {
		            for(int i=0; i<ExtraTemporaries.Length; i++)
		            {
			            if (EssentialTemporaries[i])
			            {
				            continue;
			            }
			            if (s ==  ExtraTemporaries[i])
			            {
				            if (!VerifyCorrectBlock(i))
				            {
					            continue;
				            }
				            if (ExtraMappings[i] != null)
				            {
					            return ExtraMappings[i];
				            }
				            else
				            {
					            EssentialTemporaries[i]=true;
					            if (!SuppressOutput)
					            {
					            //	throw new NotSupportedException("mapping error");
					            }
					            return "mapping error";
				            }
			            }
		            }
	            }
	            return s;
            }

            void WriteVariableDeclaration(IVariableDeclaration iVariableDeclaration)
            {
	            this.WriteType(iVariableDeclaration.VariableType, WriteName, iVariableDeclaration.Name, null, false);
            }

            void WriteVariableReferenceExpression(IVariableReferenceExpression iVariableReferenceExpression)
            {
	            string s = "";
	            Object o = iVariableReferenceExpression;
	            if (MapTemporaryName(iVariableReferenceExpression.Variable.Resolve().Name) != iVariableReferenceExpression.Variable.Resolve().Name)
	            {
		            o = null;	//TODO: references should be mapped too.
	            }
	            this.WriteReference(MapTemporaryName(iVariableReferenceExpression.Variable.Resolve().Name),s,o);
            }

            void WriteThrowExceptionStatement(IThrowExceptionStatement iThrowExceptionStatement)
            {
	            this.WriteKeyword("throw");
	            if (iThrowExceptionStatement.Expression != null)
	            {
		            this.Write(" ");
		            this.WriteExpression(iThrowExceptionStatement.Expression);
	            }
	            this.Write(";");
	            this.WriteLine();
            }

            void Write(string s)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.Write(s);
	            }
            }
            void WriteLine()
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteLine();
	            }
            }
            void WriteIndent()
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteIndent();
	            }
            }
            void WriteOutdent()
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteOutdent();
	            }
            }
            void WriteDeclaration(string s)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteDeclaration(s);
	            }
            }
            void WriteComment(string s)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteComment(s);
	            }
            }
            void WriteLiteral(string s)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteLiteral(s);
	            }
            }
            void WriteKeyword(string s)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteKeyword(s);
	            }
            }
            void WriteProperty(string s, string t)
            {
	            if (!SuppressOutput)
	            {
		            this.formatter.WriteProperty(s,t);
	            }
            }
            void WriteReference(string s, string t, Object o)
            {
	            if (!SuppressOutput)
	            {
		            string[] list = new [] 
		            {
			            "System.Runtime.InteropServices.",
			            "System.",
		            };
		            foreach(string Cut in list)
		            {
			            if (s.StartsWith(Cut))
			            {
				            s = s.Remove(0, Cut.Length);
				            break;
			            }
		            }
		            this.formatter.WriteReference(s,t,o);
	            }
            }

            public void WritePropertyDeclaration(IPropertyDeclaration iPropertyDeclaration)
            {
	            this.WriteKeyword("property");
	            this.Write(" ");
                this.WriteType<object>(iPropertyDeclaration.DeclaringType, null, null, null, false);
	            this.Write(" ");
	            this.Write(iPropertyDeclaration.Name);

                IParameterDeclarationCollection iParameterDeclarationCollection = iPropertyDeclaration.Parameters;
                if (iParameterDeclarationCollection != null)
	            {
		            WritePropertyIndices(iParameterDeclarationCollection);
	            }
	            this.Write(";");
            }

            void WritePropertyIndices(IParameterDeclarationCollection iParameterDeclarationCollection)
            {
	            if (iParameterDeclarationCollection.Count == 0)
	            {
		            return;
	            }
	            this.Write("[");
	            string separator = "";
	            foreach (IParameterDeclaration iParameterDeclaration in iParameterDeclarationCollection)
	            {
		            this.Write(separator);
                    this.WriteType<object>(iParameterDeclaration.ParameterType, null, null, null, false);
		            separator = ", ";
	            }
	            this.Write("]");
            }

            void WriteMethodReference(IMethodReference iMethodReference)
            {

	            MethodNameExt MNE = new MethodNameExt(iMethodReference.Name, null, null);	//deanwi - what if it is a constructor?
	            this.WriteReference(MNE.Name,"",iMethodReference);
	            ITypeCollection iTypeCollection = iMethodReference.GenericArguments;
	            if (iTypeCollection.Count > 0)
	            {
		            this.Write("<");
		            this.WriteTypeCollection(iTypeCollection);
		            this.Write(">");
	            }
            }

            void WritePropertyReference(IPropertyReference iPropertyReference)
            {
	            this.Write(iPropertyReference.Name);
            }

            void WriteAddressDereferenceExpression(IAddressDereferenceExpression expression)
            {
	            this.Write("*(");
	            this.WriteExpression(expression.Expression);
	            this.Write(")");
            }

            void WriteAddressOfExpression(IAddressOfExpression expression)
            {
	            this.Write("&");
	            this.WriteExpression(expression.Expression);
            }

            void WriteAddressOutExpression(IAddressOutExpression expression)
            {
            //	this.WriteKeyword("out");
            //	this.Write(" ");
	            this.WriteExpression(expression.Expression);
            }

            void WriteAddressReferenceExpression(IAddressReferenceExpression expression)
            {
            //	this.WriteKeyword("ref");
            //	this.Write(" ");
	            this.WriteExpression(expression.Expression);
            }

            void WriteArgumentListExpression(IArgumentListExpression expression)
            {
	            this.WriteKeyword("__arglist");
            }

            //how do we do this? seems to come up with boxing of value types.
            //void WriteObjectInitializeExpression(IObjectInitializeExpression iObjectInitializeExpression)
            //{
	        //    WriteType(iObjectInitializeExpression.Type, null, null, null, false);	//see how far this goes deanwi
	        //    this.Write("(");
	        //    this.Write(")");
            //}

            void WriteFieldReference(IFieldReference iFieldReference)
            {
	            this.WriteReference(iFieldReference.Name,"",iFieldReference);
            }

            void WriteFieldReferenceExpression(IFieldReferenceExpression iFieldReferenceExpression)
            {
                if (iFieldReferenceExpression.Target != null)
                {
                    ITypeReferenceExpression iTypeReferenceExpression = iFieldReferenceExpression.Target as ITypeReferenceExpression;
		            if (iTypeReferenceExpression != null)
		            {
		                this.WriteTypeReferenceExpression(iTypeReferenceExpression);
			            this.Write(".");
		            }
		            else
		            {
	                    this.WriteExpression(iFieldReferenceExpression.Target);
                        IVariableReferenceExpression iVariableReferenceExpression = iFieldReferenceExpression.Target as IVariableReferenceExpression;
			            if (iVariableReferenceExpression != null)
			            {
                            IVariableReference iVariableReference = iVariableReferenceExpression.Variable;
				            if (iVariableReference != null)
				            {
                                IVariableDeclaration iVariableDeclaration = iVariableReference.Resolve();
                                if (iVariableDeclaration != null)
					            {
						            ITypeReference iTypeReference = (iVariableDeclaration.VariableType) as ITypeReference;
                                    if (Helper.IsValueType(iTypeReference))
						            {
							            this.Write(".");
						            }
						            else
						            {
							            this.Write(".");
						            }
					            }
				            }
			            }
			            else
			            {
				            //ThisReferenceExpression
				            this.Write(".");
			            }
		            }
                }
                this.WriteFieldReference(iFieldReferenceExpression.Field);
            }

            void WriteMethodReferenceExpression(IMethodReferenceExpression iMethodReferenceExpression)
            {
	            if (iMethodReferenceExpression.Target != null)
	            {
                    ITypeReferenceExpression iTypeReferenceExpression = iMethodReferenceExpression.Target as ITypeReferenceExpression;
		            if (iTypeReferenceExpression != null)
		            {
			            this.WriteTypeReferenceExpression(iTypeReferenceExpression);
			            this.Write(".");
		            }
		            else
		            {
			            this.WriteExpression(iMethodReferenceExpression.Target);
                        IVariableReferenceExpression iVariableReferenceExpression = iMethodReferenceExpression.Target as IVariableReferenceExpression;
			            if (iVariableReferenceExpression != null)
			            {
                            IVariableReference iVariableReference = iVariableReferenceExpression.Variable;
				            if (iVariableReference != null)
				            {
                                IVariableDeclaration iVariableDeclaration = iVariableReference.Resolve();
                                if (iVariableDeclaration != null)
					            {
                                    if (Helper.IsValueType(iVariableDeclaration.VariableType as ITypeReference) 
							            || refOnStack.Contains(MapTemporaryName(iVariableReference.Resolve().Name)))
						            {
							            this.Write(".");
						            }
						            else
						            {
							            this.Write(".");
						            }
					            }
				            }
			            }
			            else
			            {
				            //ThisReferenceExpression
				            this.Write(".");
			            }
		            }
	            }
	            this.WriteMethodReference(iMethodReferenceExpression.Method);
            }

            void WriteWhileStatement(IWhileStatement iWhileStatement)
            {
                this.WriteKeyword("while");
	            this.Write(" ");
                this.Write("(");
	            if (iWhileStatement.Condition != null)
                {
		            SkipWriteLine = true;
                    this.WriteExpression(iWhileStatement.Condition);
		            SkipWriteLine = false;
                }
                this.Write(")");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                if (iWhileStatement.Body != null)
                {
                    this.WriteStatement(iWhileStatement.Body);
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteArrayIndexerExpression(IArrayIndexerExpression expression)
            {
	            this.WriteExpression(expression.Target);
	            this.Write("[");
	            string separator="";
	            foreach(IExpression iExpression in expression.Indices)
	            {
		            this.Write(separator);
		            this.WriteExpression(iExpression);
		            separator=",";
	            }
	            this.Write("]");
            }

            void WriteArgumentReferenceExpression(IArgumentReferenceExpression iArgumentReferenceExpression)
            {
	            this.WriteReference(iArgumentReferenceExpression.Parameter.Name, "", iArgumentReferenceExpression.Parameter);
            }
            void WriteThisReferenceExpression(IThisReferenceExpression iThisReferenceExpression)
            {
	            this.WriteKeyword("this");
            }
            void WritePropertyReferenceExpression(IPropertyReferenceExpression iPropertyReferenceExpression)
            {
	            if (iPropertyReferenceExpression.Target != null)
	            {
                    ITypeReferenceExpression iTypeReferenceExpression = iPropertyReferenceExpression.Target as ITypeReferenceExpression;
		            if (iTypeReferenceExpression != null)
		            {
			            this.WriteTypeReferenceExpression(iTypeReferenceExpression);
			            this.Write(".");
		            }
		            else
		            {
			            this.WriteExpression(iPropertyReferenceExpression.Target);
                        IVariableReferenceExpression iVariableReferenceExpression = iPropertyReferenceExpression.Target as IVariableReferenceExpression;
			            if (iVariableReferenceExpression != null)
			            {
                            IVariableReference iVariableReference = iVariableReferenceExpression.Variable;
				            if (iVariableReference != null)
				            {
                                IVariableDeclaration iVariableDeclaration = iVariableReference.Resolve();
                                if (iVariableDeclaration != null)
					            {
						            ITypeReference iTypeReference = (iVariableDeclaration.VariableType) as ITypeReference;
                                    if (Helper.IsValueType(iTypeReference))
						            {
							            this.Write(".");
						            }
						            else
						            {
							            this.Write(".");
						            }
					            }
				            }
			            }
			            else
			            {
				            //ThisReferenceExpression
				            this.Write(".");
			            }
		            }
	            }
	            this.WritePropertyReference(iPropertyReferenceExpression.Property);
            }
            void WriteConditionExpression(IConditionExpression iConditionExpression)
            {
	            this.Write("(");
	            this.WriteExpression(iConditionExpression.Condition);
	            this.Write(" ? ");
	            this.WriteExpression(iConditionExpression.Then);
	            this.Write(" : ");
	            this.WriteExpression(iConditionExpression.Else);
	            this.Write(")");

            }


            void WriteArrayCreateExpression(IArrayCreateExpression iArrayCreateExpression)
            {
	            this.WriteKeyword("gcnew");
	            this.Write(" ");

	            this.Write("array<");
	            this.WriteType<object>(iArrayCreateExpression.Type, null, null, null, false);
	            if (iArrayCreateExpression.Dimensions.Count > 1)
	            {
		            this.Write(", " + iArrayCreateExpression.Dimensions.Count.ToString());
	            }
	            this.Write(">");
	            this.Write("(");
	            this.WriteExpressionCollection(iArrayCreateExpression.Dimensions);
	            this.Write(")");

                //IArrayInitializerExpression iArrayInitializerExpression = iArrayCreateExpression.Initializer as IArrayInitializerExpression;
	            //if (iArrayInitializerExpression != null)
	            //{
		        //    if (iArrayInitializerExpression.Expressions.Count > 0)
		        //    {
			    //        this.Write(" = ");
			    //        this.WriteLine();
			    //        this.WriteIndent();
			    //        this.WriteExpression(iArrayCreateExpression.Initializer);
			    //        this.WriteOutdent();
		        //    }
	            //}
            }

            //void WriteArrayInitializerExpression(IArrayInitializerExpression iArrayInitializerExpression)
            //{
	        //    this.Write("{");
	        //    this.WriteLine();
	        //    this.WriteIndent();
	        //    this.WriteExpressionCollection(iArrayInitializerExpression.Expressions);
	        //    this.WriteOutdent();
	        //    this.WriteLine();
	        //    this.Write("}");
            //}

            //void WriteNamedArgumentExpression(INamedArgumentExpression iNamedArgumentExpression)
            //{
	        //    this.WriteMemberReference(iNamedArgumentExpression.Member);
	        //    this.Write("=");
	        //    this.WriteExpression(iNamedArgumentExpression.Value);
            //}

            void WriteMemberReference(IMemberReference iMemberReference)
            {
                IFieldReference iFieldReference = iMemberReference as IFieldReference;
	            if (iFieldReference != null)
	            {
		            this.WriteFieldReference(iFieldReference);
                    return;
	            }

                IMethodReference iMethodReference = iMemberReference as IMethodReference;
	            if (iMethodReference != null)
	            {
		            this.WriteMethodReference(iMethodReference);
                    return;
	            }

                IPropertyReference iPropertyReference = iMemberReference as IPropertyReference;
	            if (iPropertyReference != null)
	            {
		            this.WritePropertyReference(iPropertyReference);
                    return;
	            }

                IEventReference iEventReference = iMemberReference as IEventReference;
	            if (iEventReference != null)
	            {
		            this.WriteEventReference(iEventReference);
                    return;
	            }

	            try {
		            this.Write(iMemberReference.ToString());
	            }
	            catch(Exception e)
	            {
		            this.Write(e.ToString());
	            }
            }
            void WriteLabeledStatement(ILabeledStatement iLabeledStatement)
            {
	            this.WriteDeclaration(iLabeledStatement.Name);
	            this.Write(":");
                this.WriteLine();

	            if (iLabeledStatement.Statement != null)
	            {
		            this.WriteStatement(iLabeledStatement.Statement);
	            }
            }
            void WriteDoStatement(IDoStatement iDoStatement)
            {
                this.WriteKeyword("do");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
	            if (iDoStatement.Body != null)
                {
                    this.WriteStatement(iDoStatement.Body);
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
                this.WriteKeyword("while");
                this.Write("(");
	            if (iDoStatement.Condition != null)
                {
		            SkipWriteLine = true;
                    this.WriteExpression(iDoStatement.Condition);
		            SkipWriteLine = false;
                }
                this.Write(")");
                this.Write(";");
                this.WriteLine();
            }

            void WriteGotoStatement(IGotoStatement iGotoStatement)
            {
                this.WriteKeyword("goto");
                this.Write(" ");
	            this.WriteDeclaration(iGotoStatement.Name);
                this.Write(";");
                this.WriteLine();
            }

            void WriteForStatement(IForStatement iForStatement)
            {
                this.WriteKeyword("for");
                this.Write(" ");
                this.Write("(");
                if (iForStatement.Initializer != null)
                {
		            SkipWriteLine = true;
                    this.WriteStatement(iForStatement.Initializer);
		            SkipWriteLine = false;
	                this.Write(" ");
                }
                this.Write("; ");
	            if (iForStatement.Condition != null)
                {
		            SkipWriteLine = true;
                    this.WriteExpression(iForStatement.Condition);
		            SkipWriteLine = false;
                }
                this.Write("; ");
                if (iForStatement.Increment != null)
                {
		            SkipWriteLine = true;
                    this.WriteStatement(iForStatement.Increment);
		            SkipWriteLine = false;
                }
                this.Write(")");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                if (iForStatement.Body != null)
                {
                    this.WriteStatement(iForStatement.Body);
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteForEachStatement(IForEachStatement iForEachStatement)
            {
                this.WriteKeyword("foreach");
                this.Write(" ");
                this.Write("(");
                this.WriteVariableDeclaration(iForEachStatement.Variable);
                this.Write(" ");
                this.WriteKeyword("in");
                this.Write(" ");
	            SkipWriteLine = true;
                this.WriteExpression(iForEachStatement.Expression);
	            SkipWriteLine = false;
                this.Write(")");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                if (iForEachStatement.Body != null)
                {
                    this.WriteBlockStatement(iForEachStatement.Body);
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteBreakStatement(IBreakStatement iBreakStatement)
            {
                this.WriteKeyword("break");
                this.Write(";");
                this.WriteLine();
            }

            void WriteGenericParameterConstraintCollection(ITypeCollection parameters)
            {
                if (parameters.Count > 0)
                {
		            for (int num1 = 0; num1 < parameters.Count; num1++)
                    {
                        IGenericParameter parameter1 = (parameters[num1]) as IGenericParameter;
                        if ((parameter1 != null) && (parameter1.Constraints.Count > 0))
                        {
                            bool flag1 = true;
                            if (parameter1.Constraints.Count == 1)
                            {
                                ITypeReference reference1 = parameter1.Constraints[0] as ITypeReference;
                                if (reference1 != null)
                                {
						            flag1 = !Helper.IsObject(reference1);
                                }
                            }
                            if (flag1)
                            {
                                this.Write(" ");
                                this.WriteKeyword("where");
                                this.Write(" ");
                                this.Write(parameter1.Name);
                                this.Write(":");
                                this.Write(" ");
					            string separator = "";
                                for (int num2 = 0; num2 < parameter1.Constraints.Count; num2++)
                                {
                                    IDefaultConstructorConstraint iDefaultConstructorConstraint = parameter1.Constraints[num2] as IDefaultConstructorConstraint;
						            if (iDefaultConstructorConstraint != null)
						            {
							            continue;	//skip that, no comma
						            }
						            this.Write(separator);
                                    this.WriteType(parameter1.Constraints[num2]);
						            separator=", ";
                                }
                            }
                        }
                        if (parameter1.Attributes.Count > 0)
                        {
				            string separator = "";
                            for (int num3 = 0; num3 < parameter1.Attributes.Count; num3++)
                            {
                                ICustomAttribute attribute1 = parameter1.Attributes[num3];
					            ITypeReference tr1 = (attribute1.Constructor.DeclaringType) as ITypeReference;
					            ITypeReference tr2 = (attribute1.Constructor.DeclaringType) as ITypeReference;
					            if (Type(attribute1.Constructor.DeclaringType, "System.Runtime.CompilerServices","NewConstraintAttribute"))
					            {
						            this.Write(separator);
                                    this.WriteKeyword("gcnew");
						            this.Write("()");
						            separator=", ";
					            }
                            }
                        }
                    }
                }
            }

            void WriteTypeOfExpression(ITypeOfExpression iTypeOfExpression)
            {
                this.WriteType<object>(iTypeOfExpression.Type, null, null, null, false);
	            this.Write(".");
	            this.WriteKeyword("typeid");
            }
            void WritePropertyIndexerExpression(IPropertyIndexerExpression iPropertyIndexerExpression)
            {
	            this.WritePropertyReferenceExpression(iPropertyIndexerExpression.Target);
	            this.Write("[");
	            this.WriteExpressionCollection(iPropertyIndexerExpression.Indices);
	            this.Write("]");
            }


            void WriteLockStatement(ILockStatement statement)
            {
                this.WriteKeyword("lock");
                this.Write(" ");
                this.Write("(");
                this.WriteExpression(statement.Expression);
                this.Write(")");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                if (statement.Body != null)
                {
                    this.WriteBlockStatement(statement.Body);
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteTryCastExpression(ITryCastExpression iTryCastExpression)
            {
	            this.WriteKeyword("dynamic_cast");
                this.Write("<");
                this.WriteType<object>(iTryCastExpression.TargetType, null, null, null, false);
                this.Write(">");
                this.Write("(");
	            this.WriteExpression(iTryCastExpression.Expression);
                this.Write(")");
            }

            void WriteCanCastExpression(ICanCastExpression iCanCastExpression)
            {
                this.Write("(");
	            this.WriteKeyword("dynamic_cast");
                this.Write("<");
                this.WriteType<object>(iCanCastExpression.TargetType, null, null, null, false);
                this.Write(">");
                this.Write("(");
	            this.WriteExpression(iCanCastExpression.Expression);
                this.Write(")");
                this.Write(" != null");
                this.Write(")");
            }

            void WriteAttachEventStatement(IAttachEventStatement iAttachEventStatement)
            {
	            this.WriteEventReferenceExpression(iAttachEventStatement.Event);
                this.Write(" += ");
                this.WriteExpression(iAttachEventStatement.Listener);
                this.Write(";");
                this.WriteLine();
            }
            void WriteRemoveEventStatement(IRemoveEventStatement iRemoveEventStatement)
            {
	            this.WriteEventReferenceExpression(iRemoveEventStatement.Event);
                this.Write(" -= ");
                this.WriteExpression(iRemoveEventStatement.Listener);
                this.Write(";");
                this.WriteLine();
            }
            #if false
            void WriteStatementExpression(IStatementExpression iStatementExpression)
            {
	            SkipWriteLine = true;
	            this.Write("(");
                this.WriteStatement(iStatementExpression.Statement);
	            this.Write(")");
            }
            #endif
            void WriteSwitchStatement(ISwitchStatement iSwitchStatement)
            {
                this.WriteKeyword("switch");
                this.Write(" (");
                this.WriteExpression(iSwitchStatement.Expression);
                this.Write(")");
                this.WriteLine();
                this.Write("{");
                this.WriteLine();
                this.WriteIndent();
                foreach (ISwitchCase case1 in iSwitchStatement.Cases)
                {
                    IConditionCase iConditionCase = (case1) as IConditionCase;
                    if (iConditionCase != null)
                    {
                        this.WriteSwitchCaseCondition(iConditionCase.Condition);
                        this.Write("{");
                        this.WriteLine();
                        this.WriteIndent();
                        if (iConditionCase.Body != null)
                        {
                            this.WriteStatement(iConditionCase.Body);
                        }
                        this.WriteOutdent();
                        this.Write("}");
                        this.WriteLine();
                    }
                    IDefaultCase iDefaultCase = (case1) as IDefaultCase;
                    if (iDefaultCase != null)
                    {
                        this.WriteKeyword("default");
                        this.Write(":");
                        this.WriteLine();
                        this.Write("{");
                        this.WriteLine();
                        this.WriteIndent();
                        if (iDefaultCase.Body != null)
                        {
                            this.WriteStatement(iDefaultCase.Body);
                        }
                        this.WriteOutdent();
                        this.Write("}");
                        this.WriteLine();
                    }
                }
                this.WriteOutdent();
                this.Write("}");
                this.WriteLine();
            }

            void WriteSwitchCaseCondition(IExpression iConditionCase)
            {
                IBinaryExpression iBinaryExpression = (iConditionCase) as IBinaryExpression;
	            if (iBinaryExpression != null)
	            {
		            if (iBinaryExpression.Operator == BinaryOperator.BooleanOr)
		            {
			            this.WriteSwitchCaseCondition(iBinaryExpression.Left);
			            this.WriteSwitchCaseCondition(iBinaryExpression.Right);
		            }
	            }
	            else
	            {
                    this.WriteKeyword("case");
                    this.Write(" ");
                    this.WriteExpression(iConditionCase);
                    this.Write(":");
                    this.WriteLine();
	            }
            }

            void WriteContinueStatement(IContinueStatement iContinueStatement)
            {
                this.WriteKeyword("continue");
                this.Write(";");
                this.WriteLine();
            }

            void WriteDelegateCreateExpression(IDelegateCreateExpression iDelegateCreateExpression)
            {
	            this.Write("(IDelegateCreateExpression NYI)");
            }
            void WriteSnippetExpression(ISnippetExpression iSnippetExpression)
            {
	            this.Write("(ISnippetExpression NYI)");
            }
            public void WriteEventDeclaration(IEventDeclaration iEventDeclaration)
            {
	            this.WriteKeyword("event (NYI)");
	            this.Write(";");
            }
            void WriteEventReferenceExpression(IEventReferenceExpression iEventReferenceExpression)
            {
	            if (iEventReferenceExpression.Target != null)
	            {
                    ITypeReferenceExpression iTypeReferenceExpression = iEventReferenceExpression.Target as ITypeReferenceExpression;
		            if (iTypeReferenceExpression != null)
		            {
			            this.WriteTypeReferenceExpression(iTypeReferenceExpression);
			            this.Write(".");
		            }
		            else
		            {
			            this.WriteExpression(iEventReferenceExpression.Target);

                        IVariableReferenceExpression iVariableReferenceExpression = iEventReferenceExpression.Target as IVariableReferenceExpression;
			            if (iVariableReferenceExpression != null)
			            {
                            IVariableReference iVariableReference = iVariableReferenceExpression.Variable;
				            if (iVariableReference != null)
				            {
                                IVariableDeclaration iVariableDeclaration = iVariableReference.Resolve();
                                if (iVariableDeclaration != null)
					            {
						            ITypeReference iTypeReference = (iVariableDeclaration.VariableType) as ITypeReference;
						            if (Helper.IsValueType(iTypeReference))
						            {
							            this.Write(".");
						            }
						            else
						            {
							            this.Write(".");
						            }
					            }
				            }
			            }
			            else
			            {
				            //ThisReferenceExpression
				            this.Write(".");
			            }
		            }
	            }
            }
            void WriteEventReference(IEventReference iEventReference)
            {
	            this.Write(iEventReference.Name);
            }
            void WriteUsingStatement(IUsingStatement iUsingStatement)
            {
            }
        }
    }
}

