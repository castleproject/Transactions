// Copyright 2004-2010 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Castle.Services.Transaction
{
	using System;
	using System.ComponentModel;
	using Castle.Core.Logging;

	/// <summary>
	/// </summary>
	public class DefaultTransactionManager : MarshalByRefObject, ITransactionManager
	{
		private static readonly object TransactionCreatedEvent = new object();
		private static readonly object TransactionCommittedEvent = new object();
		private static readonly object TransactionRolledbackEvent = new object();
		private static readonly object TransactionFailedEvent = new object();
		private static readonly object TransactionDisposedEvent = new object();
		private static readonly object ChildTransactionCreatedEvent = new object();

		private readonly EventHandlerList _Events = new EventHandlerList();
		private ILogger logger = NullLogger.Instance;
		private IActivityManager activityManager;

		/// <summary>
		/// Initializes a new instance of the <see cref="DefaultTransactionManager"/> class.
		/// </summary>
		public DefaultTransactionManager() : this(new CallContextActivityManager())
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DefaultTransactionManager"/> class.
		/// </summary>
		/// <param name="activityManager">The activity manager.</param>
		public DefaultTransactionManager(IActivityManager activityManager)
		{
			if (activityManager == null) throw new ArgumentNullException("activityManager");
			this.activityManager = activityManager;
		}

		/// <summary>
		/// Gets or sets the activity manager.
		/// </summary>
		/// <value>The activity manager.</value>
		public IActivityManager ActivityManager
		{
			get { return activityManager; }
			set
			{
				if (value == null) throw new ArgumentNullException("value");
				activityManager = value;
			}
		}

		/// <summary>
		/// Gets or sets the logger.
		/// </summary>
		/// <value>The logger.</value>
		public ILogger Logger
		{
			get { return logger; }
			set { logger = value; }
		}

		#region MarshalByRefObject

		public override object InitializeLifetimeService()
		{
			return null;
		}

		#endregion

		#region ITransactionManager Members

		/// <summary>
		/// <see cref="ITransactionManager.CreateTransaction(Castle.Services.Transaction.TransactionMode,Castle.Services.Transaction.IsolationMode)"/>
		/// </summary>
		public virtual ITransaction CreateTransaction(TransactionMode transactionMode, IsolationMode isolationMode)
		{
			return CreateTransaction(transactionMode, isolationMode, false);
		}

		/// <summary>
		/// Creates a transaction.
		/// </summary>
		/// <param name="transactionMode">The transaction mode.</param>
		/// <param name="isolationMode">The isolation mode.</param>
		/// <param name="isAmbient">if set to <c>true</c>, the TM will create a distributed transaction.</param>
		/// <returns></returns>
		public virtual ITransaction CreateTransaction(TransactionMode transactionMode, IsolationMode isolationMode, bool isAmbient)
		{
			if (transactionMode == TransactionMode.Unspecified)
			{
				transactionMode = ObtainDefaultTransactionMode(transactionMode);
			}

			CheckNotSupportedTransaction(transactionMode);

			if (CurrentTransaction == null &&
			    (transactionMode == TransactionMode.Supported ||
			     transactionMode == TransactionMode.NotSupported))
			{
				return null;
			}

			TransactionBase transaction = null;

			if (CurrentTransaction != null)
			{
				if (transactionMode == TransactionMode.Requires || transactionMode == TransactionMode.Supported)
				{
					transaction = ((StandardTransaction) CurrentTransaction).CreateChildTransaction();

					logger.DebugFormat("Child Transaction {0} created", transaction.GetHashCode());
				}
			}

			if (transaction == null)
			{
				transaction = InstantiateTransaction(transactionMode, isolationMode, isAmbient);

				if (isAmbient)
				{
#if MONO
					throw new TransactionException("Distributed transactions are not supported on Mono");
#else
					transaction.Enlist(new TransactionScopeResourceAdapter(transactionMode, isolationMode));
#endif
				}

				logger.DebugFormat("Transaction {0} created", transaction.Name);
			}

			transaction.Logger = logger.CreateChildLogger(transaction.GetType().FullName);

			activityManager.CurrentActivity.Push(transaction);

			if (transaction.IsChildTransaction)
				RaiseChildTransactionCreated(transaction, transactionMode, isolationMode, isAmbient);
			else
				RaiseTransactionCreated(transaction, transactionMode, isolationMode, isAmbient);

			return transaction;
		}

		/// <summary>
		/// Factory method for creating a transaction.
		/// </summary>
		/// <param name="transactionMode">The transaction mode.</param>
		/// <param name="isolationMode">The isolation mode.</param>
		/// <param name="isAmbient">if set to <c>true</c>, the TM will create a distributed transaction.</param>
		/// <returns>A transaction</returns>
		protected virtual AbstractTransaction InstantiateTransaction(TransactionMode transactionMode, IsolationMode isolationMode, bool isAmbient)
		{
			var transaction = new TalkativeTransaction(transactionMode, isolationMode, isAmbient);
			
			new TransactionDelegate(RaiseTransactionCommitted),
				new TransactionDelegate(RaiseTransactionRolledback),
				new TransactionErrorDelegate(RaiseTransactionFailed), 
				transactionMode, isolationMode, isAmbient);
		}

		public virtual ITransaction CurrentTransaction
		{
			get { return activityManager.CurrentActivity.CurrentTransaction; }
		}

		#region events

		public event TransactionCreationInfoDelegate TransactionCreated
		{
			add { _Events.AddHandler(TransactionCreatedEvent, value); }
			remove { _Events.RemoveHandler(TransactionCreatedEvent, value); }
		}

		public event TransactionCreationInfoDelegate ChildTransactionCreated
		{
			add { _Events.AddHandler(ChildTransactionCreatedEvent, value); }
			remove { _Events.RemoveHandler(ChildTransactionCreatedEvent, value); }
		}

		public event TransactionDelegate TransactionCommitted
		{
			add { _Events.AddHandler(TransactionCommittedEvent, value); }
			remove { _Events.RemoveHandler(TransactionCommittedEvent, value); }
		}

		public event TransactionDelegate TransactionRolledback
		{
			add { _Events.AddHandler(TransactionRolledbackEvent, value); }
			remove { _Events.RemoveHandler(TransactionRolledbackEvent, value); }
		}

		public event TransactionErrorDelegate TransactionFailed
		{
			add { _Events.AddHandler(TransactionFailedEvent, value); }
			remove { _Events.RemoveHandler(TransactionFailedEvent, value); }
		}

		public event TransactionDelegate TransactionDisposed
		{
			add { _Events.AddHandler(TransactionDisposedEvent, value); }
			remove { _Events.RemoveHandler(TransactionDisposedEvent, value); }
		}

		protected void RaiseTransactionCreated(ITransaction transaction, TransactionMode transactionMode,
		                                       IsolationMode isolationMode, bool distributedTransaction)
		{
			TransactionCreationInfoDelegate eventDelegate = (TransactionCreationInfoDelegate) _Events[TransactionCreatedEvent];

			if (eventDelegate != null)
			{
				eventDelegate(transaction, transactionMode, isolationMode, distributedTransaction);
			}
		}

		protected void RaiseChildTransactionCreated(ITransaction transaction, TransactionMode transactionMode,
													IsolationMode isolationMode, bool distributedTransaction)
		{
			var eventDelegate =
				(TransactionCreationInfoDelegate) _Events[ChildTransactionCreatedEvent];

			if (eventDelegate != null)
			{
				eventDelegate(transaction, transactionMode, isolationMode, distributedTransaction);
			}
		}

		protected void RaiseTransactionFailed(ITransaction transaction, TransactionException exception)
		{
			TransactionErrorDelegate eventDelegate = (TransactionErrorDelegate)_Events[TransactionFailedEvent];
			
			if (eventDelegate != null)
			{
				eventDelegate(transaction, exception);
			}
		}

		protected void RaiseTransactionDisposed(ITransaction transaction)
		{
			TransactionDelegate eventDelegate = (TransactionDelegate) _Events[TransactionDisposedEvent];

			if (eventDelegate != null)
			{
				eventDelegate(transaction);
			}
		}

		protected void RaiseTransactionCommitted(ITransaction transaction)
		{
			TransactionDelegate eventDelegate = (TransactionDelegate) _Events[TransactionCommittedEvent];

			if (eventDelegate != null)
			{
				eventDelegate(transaction);
			}
		}

		protected void RaiseTransactionRolledback(ITransaction transaction)
		{
			TransactionDelegate eventDelegate = (TransactionDelegate) _Events[TransactionRolledbackEvent];

			if (eventDelegate != null)
			{
				eventDelegate(transaction);
			}
		}

		#endregion

		public virtual void Dispose(ITransaction transaction)
		{
			if (transaction == null)
			{
				throw new ArgumentNullException("transaction", "Tried to dispose a null transaction");
			}

			if (CurrentTransaction != transaction)
			{
				throw new ArgumentException("Tried to dispose a transaction that is not on the current active transaction", 
					"transaction");
			}

			activityManager.CurrentActivity.Pop();

			if (transaction is IDisposable)
			{
				(transaction as IDisposable).Dispose();
			}

			RaiseTransactionDisposed(transaction);

			logger.DebugFormat("Transaction {0} disposed successfully", transaction.GetHashCode());
		}

		#endregion

		protected virtual TransactionMode ObtainDefaultTransactionMode(TransactionMode transactionMode)
		{
			return TransactionMode.Requires;
		}

		private void CheckNotSupportedTransaction(TransactionMode transactionMode)
		{
			if (transactionMode == TransactionMode.NotSupported &&
			    CurrentTransaction != null &&
			    CurrentTransaction.Status == TransactionStatus.Active)
			{
				String message = "There is a transaction active and the transaction mode " +
				                 "explicit says that no transaction is supported for this context";

				logger.Error(message);

				throw new TransactionException(message);
			}
		}
	}
}