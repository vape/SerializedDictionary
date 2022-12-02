using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SerializedDict
{
    [Serializable]
    public class SerializedDictionaryDrawable
    { }

    [Serializable]
    public class SerializedDictionary<TKey, TValue> : SerializedDictionaryDrawable, IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField]
        private List<TKey> _keys = new List<TKey>();
        [SerializeField]
        private List<TValue> _values = new List<TValue>();

        [NonSerialized]
        private Dictionary<TKey, TValue> _dict = new Dictionary<TKey, TValue>();

        public ICollection<TKey> Keys => _dict.Keys;
        public ICollection<TValue> Values => _dict.Values;

        public int Count => _dict.Count;
        public bool IsReadOnly => false;

        public TValue this[TKey key]
        {
            get
            {
                return _dict[key];
            }
            set
            {
                if (!_dict.ContainsKey(key))
                {
                    _keys.Add(key);
                    _values.Add(value);
                }
                else
                {
                    for (int i = 0; i < _keys.Count; ++i)
                    {
                        var equatable = _keys[i] as IEquatable<TKey>;
                        if (equatable != null)
                        {
                            if (equatable.Equals(key))
                            {
                                _values[i] = value;
                                break;
                            }
                        }
                        else if (_keys[i].Equals(key))
                        {
                            _values[i] = value;
                            break;
                        }
                    }
                }

                _dict[key] = value;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _dict.Clear();

            for (int i = 0; i < _keys.Count; i++)
            {
                if (_keys[i] != null && !_dict.ContainsKey(_keys[i]))
                {
                    _dict.Add(_keys[i], _values[i]);
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            _dict.Add(key, value);

            _keys.Add(key);
            _values.Add(value);
        }

        public bool ContainsKey(TKey key)
        {
            return _dict.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            if (_dict.Remove(key))
            {
                var toRemove = -1;

                for (int i = 0; i < _keys.Count; ++i)
                {
                    var equatable = _keys[i] as IEquatable<TKey>;
                    if (equatable != null)
                    {
                        if (equatable.Equals(key))
                        {
                            toRemove = i;
                            break;
                        }
                    }
                    else if (_keys[i].Equals(key))
                    {
                        toRemove = i;
                        break;
                    }
                }

                if (toRemove != -1)
                {
                    _keys.RemoveAt(toRemove);
                    _values.RemoveAt(toRemove);
                }

                return true;
            }

            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dict.TryGetValue(key, out value);
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _dict.Clear();
            _keys.Clear();
            _values.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dict).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dict).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dict).Remove(item);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dict).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_dict).GetEnumerator();
        }
    }
}
