﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Data;

namespace Groves.Collections
{
	/// <summary>
	/// A collection view source implementation that supports filtering, grouping, sorting and incremental loading
	/// </summary>
	public class AdvancedCollectionViewSource : ICollectionViewEx, INotifyPropertyChanged, ISupportIncrementalLoading, IComparer<object>
	{
		private IEnumerable _source;

		private IList _sourceList;

		private INotifyCollectionChanged _sourceNcc;

		private readonly List<object> _view;

		private readonly ObservableCollection<SortDescription> _sortDescriptions;

		private readonly Dictionary<string, PropertyInfo> _sortProperties;

		private Predicate<object> _filter;

		private int _index;

		private int _deferCounter;

		/// <summary>
		/// Create a new AdvancedCollectionViewSource from an IEnumerable
		/// </summary>
		/// <param name="source">source IEnumerable</param>
		public AdvancedCollectionViewSource(IEnumerable source)
		{
			_view = new List<object>();
			_sortDescriptions = new ObservableCollection<SortDescription>();
			_sortDescriptions.CollectionChanged += _sortDescriptions_CollectionChanged;
			_sortProperties = new Dictionary<string, PropertyInfo>();
			Source = source;
		}

		/// <summary>
		/// Source
		/// </summary>
		public IEnumerable Source
		{
			get { return _source; }
			set
			{
				// ReSharper disable once PossibleUnintendedReferenceComparison
				if(_source == value) return;
				_source = value;
				_sourceList = value as IList;
				if(_sourceNcc != null) _sourceNcc.CollectionChanged -= _sourceNcc_CollectionChanged;
				_sourceNcc = _source as INotifyCollectionChanged;
				if(_sourceNcc != null) _sourceNcc.CollectionChanged += _sourceNcc_CollectionChanged;
				HandleSourceChanged();
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Manually refresh the view
		/// </summary>
		public void Refresh()
		{
			HandleSourceChanged();
		}

		private void HandleSourceChanged()
		{
			_sortProperties.Clear();
			var currentItem = CurrentItem;
			_view.Clear();
			foreach (var item in Source)
			{
				if(_filter != null && !_filter(item)) continue;
				if (_sortDescriptions.Any())
				{
					var targetIndex = _view.BinarySearch(item, this);
					if(targetIndex < 0) targetIndex = ~targetIndex;
					_view.Insert(targetIndex, item);
				}
				else
				{
					_view.Add(item);
				}
			}
			_sortProperties.Clear();
			OnVectorChanged(new VectorChangedEventArgs(CollectionChange.Reset));
			MoveCurrentTo(currentItem);
		}

		private void _sourceNcc_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (_deferCounter > 0) return;
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					if (e.NewItems.Count == 1)
					{
						HandleItemAdded(e.NewStartingIndex, e.NewItems[0]);
					}
					else
					{
						HandleSourceChanged();
					}
					break;
				case NotifyCollectionChangedAction.Remove:
					if (e.OldItems.Count == 1)
					{
						HandleItemRemoved(e.OldStartingIndex, e.OldItems[0]);
					}
					else
					{
						HandleSourceChanged();
					}
					break;
				case NotifyCollectionChangedAction.Move:
				case NotifyCollectionChangedAction.Replace:
				case NotifyCollectionChangedAction.Reset:
					HandleSourceChanged();
					break;
				default:
					//something is not OK
					//let's just fail silently...
					return;
			}
		}

		private void HandleItemAdded(int newStartingIndex, object newItem)
		{
			if(_filter != null && !_filter(newItem)) return;
			if (_sortDescriptions.Any())
			{
				_sortProperties.Clear();
				newStartingIndex = _view.BinarySearch(newItem, this);
				if (newStartingIndex < 0) newStartingIndex = ~newStartingIndex;
			}
			else if (_filter != null)
			{
				if (_sourceList == null)
				{
					HandleSourceChanged();
					return;
				}
				var visibleBelowIndex = 0;
				for (int i = newStartingIndex; i < _sourceList.Count; i++)
				{
					if (!_filter(_sourceList[i]))
					{
						visibleBelowIndex++;
					}
				}
				newStartingIndex = _view.Count - visibleBelowIndex;
			}
			_view.Insert(newStartingIndex, newItem);
			if (newStartingIndex <= _index) _index++;
			var e = new VectorChangedEventArgs(CollectionChange.ItemInserted, newStartingIndex, newItem);
			OnVectorChanged(e);
		}

		private void HandleItemRemoved(int oldStartingIndex, object oldItem)
		{
			if (_filter != null && !_filter(oldItem)) return;
			if (oldStartingIndex < 0 || oldStartingIndex >= _view.Count || !Equals(_view[oldStartingIndex], oldItem))
			{
				oldStartingIndex = _view.IndexOf(oldItem);
			}
			if(oldStartingIndex < 0) return;
			_view.RemoveAt(oldStartingIndex);
			if (oldStartingIndex <= _index) _index--;
			var e = new VectorChangedEventArgs(CollectionChange.ItemRemoved, oldStartingIndex, oldItem);
			OnVectorChanged(e);
		}

		private void _sortDescriptions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (_deferCounter > 0) return;
			HandleSourceChanged();
		}

		public IEnumerator<object> GetEnumerator() => _view.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => _view.GetEnumerator();

		public void Add(object item)
		{
			if(IsReadOnly) throw new Exception("The source is in read-only mode!");
			_sourceList.Add(item);
		}

		public void Clear()
		{
			if(IsReadOnly) throw new Exception("The source is in read-only mode!");
			_sourceList.Clear();
		}

		public bool Contains(object item) => _view.Contains(item);

		public void CopyTo(object[] array, int arrayIndex) => _view.CopyTo(array, arrayIndex);

		public bool Remove(object item)
		{
			if(IsReadOnly) throw new Exception("The source is in read-only mode!");
			_sourceList.Remove(item);
			return true;
		}

		public int Count => _view.Count;

		public bool IsReadOnly => _sourceList == null || _sourceList.IsReadOnly;

		public int IndexOf(object item) => _view.IndexOf(item);

		public void Insert(int index, object item)
		{
			if(IsReadOnly) throw new Exception("The source is in read-only mode!");
			if (_sortDescriptions.Count > 0 || _filter != null)
			{
				//no sense in inserting w/ filters or sorts, just add it
				_sourceList.Add(item);
			}
			_sourceList.Insert(index, item);
		}

		/// <summary>
		/// Removes the <see cref="T:System.Collections.Generic.IList`1"/> item at the specified index.
		/// </summary>
		/// <param name="index">The zero-based index of the item to remove.</param><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception><exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
		public void RemoveAt(int index) => Remove(_view[index]);

		/// <summary>
		/// Gets or sets the element at the specified index.
		/// </summary>
		/// <returns>
		/// The element at the specified index.
		/// </returns>
		/// <param name="index">The zero-based index of the element to get or set.</param><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception><exception cref="T:System.NotSupportedException">The property is set and the <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
		public object this[int index]
		{
			get { return _view[index]; }
			set { _view[index] = value; }
		}

		/// <summary>
		/// Occurs when the vector changes.
		/// </summary>
		public event VectorChangedEventHandler<object> VectorChanged;

		/// <summary>
		/// Move current index to item
		/// </summary>
		/// <param name="item">item</param>
		/// <returns>success of operation</returns>
		public bool MoveCurrentTo(object item) => item == CurrentItem || MoveCurrentToIndex(IndexOf(item));

		private bool MoveCurrentToIndex(int i)
		{
			if (i < -1 || i >= _view.Count) return false;
			if (i == _index) return false;
			var e = new CurrentChangingEventArgs();
			OnCurrentChanging(e);
			if (e.Cancel) return false;
			_index = i;
			OnCurrentChanged(null);
			return true;
		}

		/// <summary>
		/// Moves selected item to position
		/// </summary>
		/// <param name="index">index</param>
		/// <returns>success of operation</returns>
		public bool MoveCurrentToPosition(int index) => MoveCurrentToIndex(index);

		/// <summary>
		/// Move current item to first item
		/// </summary>
		/// <returns>success of operation</returns>
		public bool MoveCurrentToFirst() => MoveCurrentToIndex(0);

		/// <summary>
		/// Move current item to last item
		/// </summary>
		/// <returns>success of operation</returns>
		public bool MoveCurrentToLast() => MoveCurrentToIndex(_view.Count - 1);

		/// <summary>
		/// Move current item to next item
		/// </summary>
		/// <returns>success of operation</returns>
		public bool MoveCurrentToNext() => MoveCurrentToIndex(_index + 1);

		/// <summary>
		/// Move current item to previous item
		/// </summary>
		/// <returns>success of operation</returns>
		public bool MoveCurrentToPrevious() => MoveCurrentToIndex(_index - 1);

		/// <summary>
		/// Load more items from the source
		/// </summary>
		/// <param name="count">number of items to load</param>
		/// <returns>Async operation of LoadMoreItemsResult</returns>
		/// <exception cref="NotImplementedException">Not implemented yet...</exception>
		public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
		{
			throw new NotImplementedException("todo...");
		}

		/// <summary>
		/// Groups in collection
		/// </summary>
		public IObservableVector<object> CollectionGroups => null; //todo

		/// <summary>
		/// Current item
		/// </summary>
		public object CurrentItem
		{
			get { return _index > -1 && _index < _view.Count ? _view[_index] : null; }
			set { MoveCurrentTo(value); }
		}

		/// <summary>
		/// Position of current item
		/// </summary>
		public int CurrentPosition => _index;

		/// <summary>
		/// True of source has more items
		/// </summary>
		public bool HasMoreItems => false; //todo

		/// <summary>
		/// True of the current item is after the last visible item
		/// </summary>
		public bool IsCurrentAfterLast => _index >= _view.Count;

		/// <summary>
		/// True of the current item is before the first visible item
		/// </summary>
		public bool IsCurrentBeforeFirst => _index < 0;

		/// <summary>
		/// Current item changed event handler
		/// </summary>
		public event EventHandler<object> CurrentChanged;

		/// <summary>
		/// Current item changing event handler
		/// </summary>
		public event CurrentChangingEventHandler CurrentChanging;

		/// <summary>
		/// Indicates whether this CollectionView can filter its items
		/// </summary>
		public bool CanFilter => true;

		/// <summary>
		/// Predicate used to filter the visisble items
		/// </summary>
		public Predicate<object> Filter
		{
			get { return _filter; }
			set
			{
				if (_filter == value) return;
				_filter = value;
				Refresh();
			}
		}

		/// <summary>
		/// Indicates whether this CollectionView can sort its items
		/// </summary>
		public bool CanSort => true;

		/// <summary>
		/// SortDescriptions to sort the visible items
		/// </summary>
		public IList<SortDescription> SortDescriptions => _sortDescriptions;

		/// <summary>
		/// Indicates whether this CollectionView can group its items
		/// </summary>
		public bool CanGroup => false; //todo

		/// <summary>
		/// GroupDescriptions to group the visible items
		/// </summary>
		public IList<object> GroupDescriptions => null; //todo

		/// <summary>
		/// Returns the source collection
		/// </summary>
		public IEnumerable SourceCollection => _source;

		#region Events

		/// <summary>
		/// Currently selected item changing event
		/// </summary>
		/// <param name="e">event args</param>
		protected virtual void OnCurrentChanging(CurrentChangingEventArgs e)
		{
			if (_deferCounter > 0) return;
			CurrentChanging?.Invoke(this, e);
		}

		/// <summary>
		/// Currently selected item changed event
		/// </summary>
		/// <param name="e">event args</param>
		protected virtual void OnCurrentChanged(object e)
		{
			if (_deferCounter > 0) return;
			CurrentChanged?.Invoke(this, e);
			// ReSharper disable once ExplicitCallerInfoArgument
			OnPropertyChanged(nameof(CurrentItem));
		}

		/// <summary>
		/// Vector changed event
		/// </summary>
		/// <param name="e">event args</param>
		protected virtual void OnVectorChanged(IVectorChangedEventArgs e)
		{
			if (_deferCounter > 0) return;
			VectorChanged?.Invoke(this, e);
			// ReSharper disable once ExplicitCallerInfoArgument
			OnPropertyChanged(nameof(Count));
		}

		#endregion Events

		#region Comparer

		/// <summary>
		/// IComparer implementation
		/// </summary>
		/// <param name="x">Object A</param>
		/// <param name="y">Object B</param>
		/// <returns>Comparison value</returns>
		int IComparer<object>.Compare(object x, object y)
		{
			if (!_sortProperties.Any())
			{
				var typeInfo = x.GetType().GetTypeInfo();
				foreach (var sd in _sortDescriptions)
				{
					_sortProperties[sd.PropertyName] = typeInfo.GetDeclaredProperty(sd.PropertyName);
				}
			}
			foreach (var sd in _sortDescriptions)
			{
				var pi = _sortProperties[sd.PropertyName];
				var cx = pi.GetValue(x) as IComparable;
				var cy = pi.GetValue(y) as IComparable;
				try
				{
					// ReSharper disable once PossibleUnintendedReferenceComparison
					var cmp = cx == cy ? 0 : cx == null ? -1 : cy == null ? +1 : cx.CompareTo(cy);
					if (cmp != 0)
					{
						return sd.Direction == SortDirection.Ascending ? +cmp : -cmp;
					}
				}
				catch
				{
					//fail silently
				}
			}
			return 0;
		}

		#endregion Comparer

		#region Defer refresh

		/// <summary>
		/// Stops refreshing until it is disposed
		/// </summary>
		/// <returns>An disposable object</returns>
		public IDisposable DeferRefresh()
		{
			return new NotificationDeferrer(this);
		}

		/// <summary>
		/// Notification deferrer helper class
		/// </summary>
		public class NotificationDeferrer : IDisposable
		{
			private readonly AdvancedCollectionViewSource _acvs;
			private readonly object _currentItem;

			/// <summary>
			/// Create a notification deferrer
			/// </summary>
			/// <param name="acvs">Source ACVS</param>
			public NotificationDeferrer(AdvancedCollectionViewSource acvs)
			{
				_acvs = acvs;
				_currentItem = _acvs.CurrentItem;
				_acvs._deferCounter++;
			}

			/// <summary>
			/// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
			/// </summary>
			/// <filterpriority>2</filterpriority>
			public void Dispose()
			{
				_acvs.MoveCurrentTo(_currentItem);
				_acvs._deferCounter--;
				_acvs.Refresh();
			}
		}

		#endregion Defer refresh

		#region INotifyPropertyChanged

		/// <summary>
		/// Occurs when a property value changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Property changed event invoker
		/// </summary>
		/// <param name="propertyName">name of the property that changed</param>
		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion INotifyPropertyChanged
	}
}