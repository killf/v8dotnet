namespace V8.Net
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Implements a way to store managed objects which can be tracked by index for quick lookup.
    /// <param>Note: The indexes are similar to native pointers, and thus, an index is REQUIRED in order to retrieve or remove a reference.</param>
    /// </summary>
    public class IndexedObjectList<T> : IEnumerable<T> where T : class
    {
        struct ObjectItem { public T Object; public int ValidObjectIndex; }
        struct ValidObjectItem { public T Object; public int ObjectIndex; } // ('ObjectIndex' is a "link-back" to allow fast swapping of valid object items when an object item is removed.)

        /// <summary>
        /// Allows enumerating 
        /// </summary>
        public IEnumerable<T> Objects
        {
            get { return m_validObjects.Select(o => o.Object); }
        }

        private readonly List<ObjectItem> m_objects;

        private readonly List<ValidObjectItem> m_validObjects; // (a list of only valid objects in the objects list)

        private readonly List<int> m_unusedIndexes; // (when an object other than the last is removed, the indexes are stored here for quick lookup)

        /// <summary>
        /// The number of managed references indexed in this instance.
        /// <para>Warning: this is NOT the indexed object array size.</para>
        /// </summary>
        public int Count { get { return m_objects.Count - m_unusedIndexes.Count; } }

        /// <summary>
        /// The current size of the list which stores the object references (this can be much larger that the actual number of objects stored).
        /// </summary>
        public int ObjectIndexListCount { get { return m_objects.Count; } }

        /// <summary>
        /// Gets or sets the capacity of the internal reference list.  You cannot set a capacity to less than the last object in the list.
        /// </summary>
        public int Capacity
        {
            get
            {
                return m_objects.Capacity;
            }
            set {
                if (value < Count) return;
                m_objects.Capacity = value; m_unusedIndexes.TrimExcess();
            }
        }

        /// <summary>
        /// The number of unused index positions.
        /// This occurs when an object is removed from within the list, and not from the end.
        /// <para>Note: Fragmenting does not slow down any operations.</para>
        /// </summary>
        public int UnusedIndexCount
        {
            get { return m_unusedIndexes.Count; }
        }

        /// <summary>
        /// The percentage amount of unused index positions as compared to the total object list length.
        /// <para>When the last object in the internal reference list is not removed when removing other objects, the internal reference list count cannot be reduced.
        /// In such case, another list keeps track of the unused index positions for quick lookup.
        /// The fragmentation value is a simple percentage of "unused index positions" to "internal reference list count".</para>]
        /// <para>Note: This value is for informative purposes only.  Fragmentation does not reduce the speed additions, removals, or lookups;
        /// However, the internal reference list cannot reduce capacity when fragmentation exists.</para>
        /// </summary>
        public double Fragmentation { get { return m_objects.Count > 0 ? (double)m_unusedIndexes.Count / m_objects.Count * 100 : 0d; } }

        /// <summary>
        /// Returns the object at the given object index.
        /// </summary>
        public T this[int i]
        {
            get
            {
                if (i < 0 || i >= m_objects.Count)
                    return null;
                //??throw new IndexOutOfRangeException("IndexedObjectList: this[ID: " + i + "] is out of range (current list size is " + _Objects.Count + ").");

                return m_objects[i].Object;
            }
            set
            {
                lock (this)
                {
                    var item = m_objects[i];
                    item.Object = value;
                    m_objects[i] = item;
                }
            }
        }

        /// <summary>
        /// Create an indexed object list with the given capacity.
        /// <para>Warning: There are 3 lists created internally with the given capacity:  One for indexed storage, one for quick query of consecutive items, and
        /// another for unused index positions.  These 3 lists ensure maximum speed for most needed operations.</para>
        /// </summary>
        /// <param name="capacity">The capacity allows to pre-allocate the internal list's initial length to reduce the need to keep reallocating memory as objects are added.</param>
        public IndexedObjectList() : this(1000)
        {
        }

        /// <summary>
        /// Create an indexed object list with the given capacity.
        /// <para>Warning: There are 3 lists created internally with the given capacity:  One for indexed storage, one for quick query of consecutive items, and
        /// another for unused index positions.  These 3 lists ensure maximum speed for most needed operations.</para>
        /// </summary>
        /// <param name="capacity">The capacity allows to pre-allocate the internal list's initial length to reduce the need to keep reallocating memory as objects are added.</param>
        public IndexedObjectList(int capacity)
        {
            m_objects = new List<ObjectItem>(capacity);
            m_unusedIndexes = new List<int>(capacity); // (when an object other than the last is removed, the indexes are stored here for quick lookup)
            m_validObjects = new List<ValidObjectItem>(capacity);
        }

        /// <summary>
        /// Add object and return the index position.
        /// </summary>
        public int Add(T obj)
        {
            // ... check for unused indexes first ...

            if (m_unusedIndexes.Count > 0)
            {
                var lastIndex = m_unusedIndexes.Count - 1;
                var i = m_unusedIndexes[lastIndex];
                m_unusedIndexes.RemoveAt(lastIndex);
                m_objects[i] = new ObjectItem { Object = obj, ValidObjectIndex = m_validObjects.Count };
                m_validObjects.Add(new ValidObjectItem { Object = obj, ObjectIndex = i });
                return i;
            }
            else
            {
                var i = m_objects.Count;
                m_objects.Add(new ObjectItem { Object = obj, ValidObjectIndex = m_validObjects.Count });
                m_validObjects.Add(new ValidObjectItem { Object = obj, ObjectIndex = i });
                return i;
            }
        }

        /// <summary>
        /// Removes the object at the given index and sets the entry to 'default(T)' (which is usually 0 or null).
        /// </summary>
        public void Remove(int objIndex)
        {
            if (objIndex < 0 || objIndex >= m_objects.Count)
                throw new IndexOutOfRangeException("objIndex = " + objIndex);

            var itemRemoved = m_objects[objIndex];

            if (itemRemoved.ValidObjectIndex == -1)
                return; // (already deleted, nothing to do)

            if (objIndex == m_objects.Count - 1) // (if removing from end, simply "pop" it)
            {
                m_objects.RemoveAt(objIndex);
            }
            else // (clear this entry and record it for reuse)
            {
                m_objects[objIndex] = new ObjectItem { Object = default(T), ValidObjectIndex = -1 };
                m_unusedIndexes.Add(objIndex);
            }

            // ... swap valid-object-list entry with end and remove it ...

            var voLastIndex = m_validObjects.Count - 1;

            if (itemRemoved.ValidObjectIndex != voLastIndex) // (if the item removed references the last "valid object" entry, just simply remove it [skip the block])
            {
                var voEnd = m_validObjects[voLastIndex];
                m_validObjects[itemRemoved.ValidObjectIndex] = voEnd; // (move end entry in place of removed item)

                var itemToUpdate = m_objects[voEnd.ObjectIndex]; // (need to update the indexed object's "valid object" index via the link-back index)
                itemToUpdate.ValidObjectIndex = itemRemoved.ValidObjectIndex;
                m_objects[voEnd.ObjectIndex] = itemToUpdate;
            }

            m_validObjects.RemoveAt(voLastIndex); // (last item is moved down now in place of the deleted object, so this just really decrements the count.)
        }

        /// <summary>
        /// The internal lists never shrink capacity for speed reasons.
        /// If a large number of objects where added and later removed, call this method to shrink the memory used (if possible).
        /// </summary>
        public void Compact() { m_objects.TrimExcess(); m_validObjects.TrimExcess(); m_unusedIndexes.TrimExcess(); }

        /// <summary>
        /// Clears the internal lists and resets them to default initial capacity.
        /// </summary>
        public void Clear() { m_objects.Clear(); m_validObjects.Clear(); m_unusedIndexes.Clear(); }

        public IEnumerator<T> GetEnumerator()
        {
            return Objects.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Objects.GetEnumerator();
        }
    }
}
