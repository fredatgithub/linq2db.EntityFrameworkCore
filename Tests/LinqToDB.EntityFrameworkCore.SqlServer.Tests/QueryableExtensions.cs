﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;

namespace LinqToDB.EntityFrameworkCore.SqlServer.Tests
{
	public static class QueryableExtensions
	{
		public static IQueryable<T> AnyFromItems<T, TItem>(this IQueryable<T> source, IEnumerable<TItem> items,
			string propName)
		{
			var entityParam = Expression.Parameter(typeof(T), "e");
			var itemParam = Expression.Parameter(typeof(TItem), "i");

			var anyPredicate =
				Expression.Lambda(
					Expression.Equal(itemParam, Expression.PropertyOrField(entityParam, propName)),
					itemParam);

			var filterLambda =
				Expression.Lambda<Func<T, bool>>(
					Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), new[] { typeof(TItem) },
						Expression.Constant(items), anyPredicate),
					entityParam);

			return source.Where(filterLambda);
		}

		public static IQueryable<T> AnyFromItems<T>(this IQueryable<T> source, string items, string propName)
		{
			var strItems = items.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			var entityParam = Expression.Parameter(typeof(T), "e");
			var propExpression = Expression.PropertyOrField(entityParam, propName);
			var itemType = propExpression.Type;
			var itemParam = Expression.Parameter(itemType, "i");

			var anyPredicate =
				Expression.Lambda(
					Expression.Equal(itemParam, Expression.PropertyOrField(entityParam, propName)),
					itemParam);

			// apply conversion
			var itemsExpression = Expression.Call(typeof(QueryableExtensions), nameof(QueryableExtensions.ParseItems),
				new[] { itemType }, Expression.Constant(strItems));

			var filterLambda =
				Expression.Lambda<Func<T, bool>>(
					Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), new[] { itemType },
						itemsExpression, anyPredicate),
					entityParam);

			return source.Where(filterLambda);
		}

		private static IEnumerable<TItem> ParseItems<TItem>(IEnumerable<string> items)
		{
			return items.Select(i => (TItem)Convert.ChangeType(i, typeof(TItem)));
		}

		public static async Task<IEnumerable<T>> FilterExistentAsync<T, TProp>(this ICollection<T> items,
			IQueryable<T> dbQuery, Expression<Func<T, TProp>> prop, CancellationToken cancellationToken = default)
		{
			var propGetter = prop.Compile();
			var ids = items.Select(propGetter).ToList();
			var parameter = prop.Parameters[0];

			var predicate = Expression.Call(typeof(Enumerable), "Contains", new[] { typeof(TProp) }, Expression.Constant(ids), prop.Body);
			var predicateLambda = Expression.Lambda(predicate, parameter);

			var filtered = Expression.Call(typeof(Queryable), "Where", new[] {typeof(T)}, dbQuery.Expression,
				predicateLambda);

			var selectExpr = Expression.Call(typeof(Queryable), "Select", new[] {typeof(T), typeof(TProp)}, filtered, prop);
			var selectQuery = dbQuery.Provider.CreateQuery<TProp>(selectExpr);

			var existingIds = await selectQuery.ToListAsync(cancellationToken);

			return items.Where(i => !existingIds.Contains(propGetter(i)));
		}

		public static IIncludableQueryable<TEntity, TProp> IncludeInline<TEntity, TProp, TInlineProp>(
			this IIncludableQueryable<TEntity, TProp> includable, Expression<Func<TEntity, TInlineProp>> inlineProp)
		{
			var path = new List<Expression>();

			var current = includable.Expression;
			while (current.NodeType == ExpressionType.Call)
			{
				var mc = (MethodCallExpression) current;
				if (mc.Method.Name == "Include" || mc.Method.Name == "ThenInclude")
					path.Add(mc.Arguments[1]);
				else
					break;
			}

			return includable;
		}

		public static Expression<Func<T, bool>> MakePropertiesPredicate<T, TValue>(Expression<Func<TValue, TValue, bool>> pattern, TValue searchValue, bool isOr)
		{
			var parameter = Expression.Parameter(typeof(T), "e");
			var searchExpr = Expression.Constant(searchValue);

			var predicateBody = typeof(T).GetProperties()
				.Where(p => p.PropertyType == typeof(TValue))
				.Select(p =>
					ExpressionReplacer.GetBody(pattern, Expression.MakeMemberAccess(
						parameter, p), searchExpr))
				.Aggregate(isOr ? Expression.OrElse : Expression.AndAlso);

			return Expression.Lambda<Func<T, bool>>(predicateBody, parameter);
		}

		public static IQueryable<T> FilterByProperties<T, TValue>(this IQueryable<T> query, TValue searchValue,
			Expression<Func<TValue, TValue, bool>> pattern, bool isOr)
		{
			return query.Where(MakePropertiesPredicate<T, TValue>(pattern, searchValue, isOr));
		}

		sealed class ExpressionReplacer : ExpressionVisitor
		{
			readonly IDictionary<Expression, Expression> _replaceMap;

			public ExpressionReplacer(IDictionary<Expression, Expression> replaceMap)
			{
				_replaceMap = replaceMap ?? throw new ArgumentNullException(nameof(replaceMap));
			}

			[return: NotNullIfNotNull(nameof(node))]
			public override Expression? Visit(Expression? node)
			{
				if (node != null && _replaceMap.TryGetValue(node, out var replacement))
					return replacement;
				return base.Visit(node);
			}

			public static Expression Replace(Expression expr, Expression toReplace, Expression toExpr)
			{
				return new ExpressionReplacer(new Dictionary<Expression, Expression> { { toReplace, toExpr } }).Visit(expr);
			}

			public static Expression Replace(Expression expr, IDictionary<Expression, Expression> replaceMap)
			{
				return new ExpressionReplacer(replaceMap).Visit(expr);
			}

			public static Expression GetBody(LambdaExpression lambda, params Expression[] toReplace)
			{
				if (lambda.Parameters.Count != toReplace.Length)
					throw new InvalidOperationException();

				return new ExpressionReplacer(Enumerable.Range(0, lambda.Parameters.Count)
					.ToDictionary(i => (Expression)lambda.Parameters[i], i => toReplace[i])).Visit(lambda.Body);
			}
		}
	}
}
