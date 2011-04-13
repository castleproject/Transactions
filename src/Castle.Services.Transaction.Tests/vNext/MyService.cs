﻿#region license

// // Copyright 2009-2011 Henrik Feldt - http://logibit.se /
// // 
// // Licensed under the Apache License, Version 2.0 (the "License");
// // you may not use this file except in compliance with the License.
// // You may obtain a copy of the License at
// // 
// //     http://www.apache.org/licenses/LICENSE-2.0
// // 
// // Unless required by applicable law or agreed to in writing, software
// // distributed under the License is distributed on an "AS IS" BASIS,
// // WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// // See the License for the specific language governing permissions and
// // limitations under the License.

#endregion

using System;
using System.Diagnostics.Contracts;
using Castle.Services.vNextTransaction;
using NUnit.Framework;

namespace Castle.Services.Transaction.Tests.vNext
{
	public class MyService : IMyService
	{
		private readonly ITxManager _Manager;

		public MyService(ITxManager manager)
		{
			Contract.Ensures(_Manager != null);
			_Manager = manager;
		}

		[vNextTransaction.Transaction]
		public vNextTransaction.ITransaction VerifyInAmbient()
		{
			Assert.That(System.Transactions.Transaction.Current != null,
			            "The current transaction mustn't be null.");

			Assert.That(_Manager.CurrentTransaction != null,
				"The current transaction in the transaction manager mustn't be null.");

			return _Manager.CurrentTransaction.Value;
		}

		[vNextTransaction.Transaction]
		public void VerifyInAmbient(Action a)
		{
			Assert.That(System.Transactions.Transaction.Current != null,
			            "The current transaction mustn't be null.");

			a();
		}
	}
}