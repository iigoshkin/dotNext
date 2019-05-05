using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DebugInfoGenerator = System.Runtime.CompilerServices.DebugInfoGenerator;

namespace DotNext.Metaprogramming
{
    using static Collections.Generic.Collection;
    using static Reflection.DelegateType;
    using Runtime.CompilerServices;

    /// <summary>
    /// Represents lambda function builder.
    /// </summary>
    internal abstract class LambdaExpression : LexicalScope
    {
        private protected readonly bool tailCall;

        private protected LambdaExpression(bool tailCall) : base(false) => this.tailCall = tailCall;

        private protected IReadOnlyList<ParameterExpression> GetParameters(System.Reflection.ParameterInfo[] parameters)
            => Array.ConvertAll(parameters, parameter => Expression.Parameter(parameter.ParameterType, parameter.Name));

        /// <summary>
        /// Gets recursive reference to the lambda.
        /// </summary>
        /// <remarks>
        /// This property can be used to make recursive calls.
        /// </remarks>
        internal abstract Expression Self
        {
            get;
        }

        /// <summary>
        /// Gets lambda parameters.
        /// </summary>
        internal abstract IReadOnlyList<ParameterExpression> Parameters { get; }

        internal abstract Expression Return(Expression result);

        private protected LambdaCompiler<D> CreateCompiler<D>(Expression<D> lambda)
            where D : Delegate
        {
            var document = SymbolDocument;
            if(document is null)    //debugging not enabled
                return new LambdaCompiler<D>(lambda);
            //compile temporary version of lambda
            //compilation causes source code generation
            var generator = DebugInfoGenerator.CreatePdbGenerator();
            lambda.Compile(generator);
            //now adjust debugging information
            var rewriter = new DebugInfoRewriter(document);
            lambda = (Expression<D>)rewriter.Visit(lambda);
            return new LambdaCompiler<D>(lambda, generator);
        }
    }

    /// <summary>
    /// Represents lambda function builder.
    /// </summary>
    /// <typeparam name="D">The delegate describing signature of lambda function.</typeparam>
    internal sealed class LambdaExpression<D> : LambdaExpression, ILexicalScope<LambdaCompiler<D>, Action<LambdaContext>>, ILexicalScope<LambdaCompiler<D>, Action<LambdaContext, ParameterExpression>>, ILexicalScope<LambdaCompiler<D>, Func<LambdaContext, Expression>>
        where D : Delegate
    {        
        private ParameterExpression recursion;
        private ParameterExpression lambdaResult;
        private LabelTarget returnLabel;

        private readonly Type returnType;

        internal LambdaExpression(bool tailCall)
            : base(tailCall)
        {
            if (typeof(D).IsAbstract)
                throw new GenericArgumentException<D>(ExceptionMessages.AbstractDelegate, nameof(D));
            var invokeMethod = GetInvokeMethod<D>();
            Parameters = GetParameters(invokeMethod.GetParameters());
            returnType = invokeMethod.ReturnType;
        }

        /// <summary>
        /// Gets recursive reference to the lambda.
        /// </summary>
        /// <remarks>
        /// This property can be used to make recursive calls.
        /// </remarks>
        internal override Expression Self
        {
            get
            {
                if (recursion is null)
                    recursion = Expression.Variable(typeof(D), "self");
                return recursion;
            }
        }

        /// <summary>
        /// Gets lambda parameters.
        /// </summary>
        internal override IReadOnlyList<ParameterExpression> Parameters { get; }

        private ParameterExpression Result
        {
            get
            {
                if (returnType == typeof(void))
                    return null;
                else if (lambdaResult is null)
                    DeclareVariable(lambdaResult = Expression.Variable(returnType, "result"));
                return lambdaResult;
            }
        }

        internal override Expression Return(Expression result)
        {
            if (returnLabel is null)
                returnLabel = Expression.Label("leave");
            if (result is null)
                result = Expression.Default(returnType);
            result = returnType == typeof(void) ? (Expression)Expression.Return(returnLabel) : Expression.Block(Expression.Assign(Result, result), Expression.Return(returnLabel));
            return result;
        }

        private new Expression<D> Build()
        {
            if (!(returnLabel is null))
                AddStatement(Expression.Label(returnLabel));
            //last instruction should be always a result of a function
            if (!(lambdaResult is null))
                AddStatement(lambdaResult);
            //rewrite body
            var body = Expression.Block(returnType, Variables, this);
            //build lambda expression
            if (!(recursion is null))
                body = Expression.Block(Sequence.Singleton(recursion),
                    Expression.Assign(recursion, Expression.Lambda<D>(body, tailCall, Parameters)),
                    Expression.Invoke(recursion, Parameters));
            return Expression.Lambda<D>(body, tailCall, Parameters);
        }

        public LambdaCompiler<D> Build(Action<LambdaContext> scope)
        {
            using(var context = new LambdaContext(this))
                scope(context);
            return CreateCompiler(Build());
        }

        public LambdaCompiler<D> Build(Action<LambdaContext, ParameterExpression> scope)
        {
            using(var context = new LambdaContext(this))
                scope(context, Result);
            return CreateCompiler(Build());
        }

        public LambdaCompiler<D> Build(Func<LambdaContext, Expression> body)
        {
            using(var context = new LambdaContext(this))
                AddStatement(body(context));
            return CreateCompiler(Build());
        }

        public override void Dispose()
        {
            lambdaResult = null;
            returnLabel = null;
            recursion = null;
            base.Dispose();
        }
    }
}