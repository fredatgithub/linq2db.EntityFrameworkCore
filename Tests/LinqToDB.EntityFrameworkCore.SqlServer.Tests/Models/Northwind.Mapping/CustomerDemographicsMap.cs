﻿using LinqToDB.EntityFrameworkCore.BaseTests.Models.Northwind;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LinqToDB.EntityFrameworkCore.SqlServer.Tests.Models.Northwind.Mapping
{
	public class CustomerDemographicsMap : BaseEntityMap<CustomerDemographics>
	{
		public override void Configure(EntityTypeBuilder<CustomerDemographics> builder)
		{
			base.Configure(builder);

			builder.HasKey(e => e.CustomerTypeId)
				   .IsClustered(false);

			builder.Property(e => e.CustomerTypeId)
				.HasColumnName("CustomerTypeID")
				.HasMaxLength(10)
				.ValueGeneratedNever();

			builder.Property(e => e.CustomerDesc).HasColumnType("ntext");
		}
	}
}
