using System;
using System.Text;
using System.Collections;
using System.Security.Cryptography;

using Brunet;

namespace Brunet.Dht {


public class TableServer {

  class TableKey {
    protected readonly byte[] _buf;

    public byte[] Buffer { 
      get { 
	return _buf;
      } 
    }

    public TableKey(byte[] buffer) {
      _buf = buffer;
    }
    public override bool Equals(Object o) {
      TableKey other = o as TableKey;
      if (other == null) {
	return false;
      }
      
      if (other.Buffer.Length != Buffer.Length) {
	return false;
      }
      for (int i = 0; i < Buffer.Length; i++) {
	if (Buffer[i] != other.Buffer[i]) {
	  return false;
	}
      }
      return true;
    }
    public override int GetHashCode() {
      try {
      int hash = 0;
      if (Buffer.Length >= 4) {


	hash = Brunet.NumberSerializer.ReadInt(Buffer, Buffer.Length - 4);
#if DHT_DEBUG
	Console.WriteLine("Hashcode returned is: {0}", hash);
#endif
	return hash;
      }
      hash = Brunet.NumberSerializer.ReadInt(Buffer, 0);
#if DHT_DEBUG
      Console.WriteLine("Hashcode returned is: {0}", hash);
#endif
      return hash;
      } catch (Exception e) {
#if DHT_DEBUG
	Console.WriteLine("Exception in GetHashCode(): {0}", e);
#endif
	//this is arbitrary
	return 1;
      }
    }
    public AHAddress GetTargetAddress() {
      HashAlgorithm hashAlgo = HashAlgorithm.Create();
      byte[] hash = hashAlgo.ComputeHash(Buffer);
      hash[Address.MemSize -1] &= 0xFE;
      AHAddress target = new AHAddress(new BigInteger(hash));
      return target;
    }
  }  
  
  
  protected object _sync;
  
  //maintain a list of keys that are expiring:
  //list of keys sorted on expiration times
  protected ArrayList _expiring_entries;

  protected Hashtable _ht;
  protected int _max_idx;

  protected Node _node;
  public TableServer(Node node) {
    /**
     * @todo make sure there is a second copy of all data
     * in the network.  When a neighbor is lost, make sure
     * the new neighbor is updated with the correct content
     */
    _sync = new object();
    _node = node;
    _expiring_entries = new ArrayList();
    _ht = new Hashtable();
    _max_idx = 0;
  }

  protected  bool ValidatePasswordFormat(string password, 
					 out string hash_name,
					 out string base64_val) {
    
    string[] ss = password.Split(new char[] {':'});
    if (ss.Length != 2) {
      hash_name = "invalid";
      base64_val = null;
      return false;
    }
    hash_name = ss[0];
    base64_val = ss[1];
    return true;
  }

  public int GetCount() {
    int count = 0;
    lock(_sync) {
      //delete keys that have expired
      DeleteExpired();
#if DHT_DEBUG
      Console.WriteLine("[DhtServer] Cleaned up expired entries.");
#endif
      foreach (Object val in _ht.Values) 
      {
	ArrayList entry_list = (ArrayList) val;
	count += entry_list.Count;
      }
    }
    //Alternatively, we could also have count as number of keys
    //return _ht.Count;

    return count;
  }
  public int Put(byte[] key, int ttl, string hashed_password, byte[] data) {
#if DHT_DEBUG
    Console.WriteLine("[DhtServer]: Put().");
#endif

    string hash_name = null;
    string base64_val = null;
    if (!ValidatePasswordFormat(hashed_password, out hash_name, 
				out base64_val)) {
      throw new Exception("Invalid password format.");
    }
    
    DateTime create_time = DateTime.Now;
    TimeSpan ts = new TimeSpan(0,0,ttl);
    DateTime end_time = create_time + ts;
   
    lock(_sync) {
      //delete all keys that have expired
      DeleteExpired();
#if DHT_DEBUG
      Console.WriteLine("[DhtServer] Cleaned up expired entries.");
#endif
      TableKey ht_key = new TableKey(key);
      ArrayList entry_list = (ArrayList)_ht[ht_key];
      if( entry_list != null ) {
        //Make sure we only keep one reference to a key to save memory:
	//Arijit Ganguly - I had no idea what this was about. Now I know...
#if DHT_DEBUG
	Console.WriteLine("[DhtServer]: Key exists.");
#endif
        key = ((Entry)entry_list[0]).Key;
      }
      else {
        //This is a new key:
        entry_list = new ArrayList();
	//added the new TableKey to hashtable
#if DHT_DEBUG
	Console.WriteLine("[DhtServer]: Key doesn't exist. Created new entry_list.");
#endif
	_ht[ht_key] = entry_list;
      }
      _max_idx++; //Increment the maximum index

      foreach(Entry ent in entry_list) {
	if (ent.Password.Equals(hashed_password)) {
#if DHT_DEBUG
	  Console.WriteLine("[DhtServer]: Attempting to duplicate. (No put).");
#endif
	  return entry_list.Count;
	}
      }


      //Look up 
      Entry e = new Entry(key, hashed_password,  create_time, end_time,
                         data, _max_idx);
      
      //Add the entry to the end of the list.
      entry_list.Add(e);
      //Further add this to sorted list _expired_entries list
      InsertToSorted(e);
     
      ///@todo, we might need to tell a neighbor about this object
      return entry_list.Count;
    }
  }

  /**
   * This method differs from put() in the key is already mapped
   * we fail
   * @param key key associated with the date item
   * @param ttl time-to-live in seconds
   * @param hashed_password <hash_name>:<base64(hashed_pass)>
   * @param data data associated with the key
   * @return true on success, false on failure
   */
  
  public bool Create(byte[] key, int ttl, string hashed_password, byte[] data) 
  {

#if DHT_DEBUG
    Console.WriteLine("[DhtServer]: Create().");
#endif
    string hash_name = null;
    string base64_val = null;
    if (!ValidatePasswordFormat(hashed_password, out hash_name,
				out base64_val)) {
      throw new Exception("Invalid password format.");
    }
    DateTime create_time = DateTime.Now;
    TimeSpan ts = new TimeSpan(0,0,ttl);
    DateTime end_time = create_time + ts;
    
    lock(_sync) {
      //delete all keys that have expired
      DeleteExpired();
#if DHT_DEBUG
      Console.WriteLine("[DhtServer] Cleaned up expired entries.");
#endif
      TableKey ht_key = new TableKey(key);
      ArrayList entry_list = (ArrayList)_ht[ht_key];
      if( entry_list != null ) {
#if DHT_DEBUG
	Console.WriteLine("[DhtServer]: No duplication allowed. Key exists.");
#endif
	//we already have the key mapped to something. return false
	throw new Exception("Attempting to re-create key. Entry exists. ");
      }
      //This is a new key:
      entry_list = new ArrayList();
      _ht[ht_key] = entry_list;
#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: Key doesn't exist. Created new entry_list.");
#endif      
      _max_idx++; //Increment the maximum index
      //Look up 
      Entry e = new Entry(key, hashed_password,  create_time, end_time,
			  data, _max_idx);
      //Add the entry to the end of the list.
      entry_list.Add(e);
      //Further add the entry to the sorted list _expired_entries
      InsertToSorted(e);
      
      ///@todo, we might need to tell a neighbor about this object
      return true;
    } //release the lock
  }

  public IList Get(byte[] key, int maxbytes, byte[] token)
  {

#if DHT_DEBUG
    Console.WriteLine("[DhtServer]: Get().");
#endif

    int seen_start_idx = -1;
    int seen_end_idx = -1;
    if( token != null ) {
      
      //This is a continuing get...
      //This should be an array of ints:
      int[] bounds = (int[])AdrConverter.Deserialize(new System.IO.MemoryStream(token));
      seen_start_idx = bounds[0];
      seen_end_idx = bounds[1];
#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: seen_start_idx: " + seen_start_idx);
      Console.WriteLine("[DhtServer]: seen_end_idx: " + seen_end_idx);
#endif
    } else {
#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: null token.");
#endif  
    }
    int consumed_bytes = 0;
    
    ArrayList result = new ArrayList();
    ArrayList values = new ArrayList();
    int remaining_items = 0;
    byte[] next_token = null;
    
    lock(_sync ) { 
    //delete keys that have expired
    DeleteExpired();
#if DHT_DEBUG
      Console.WriteLine("[DhtServer] Cleaned up expired entries.");
#endif      

    TableKey ht_key = new TableKey(key);  
    ArrayList entry_list = (ArrayList)_ht[ht_key];

    int seen = 0; //Number we have already seen for this key
    if( entry_list != null ) {
#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: Key exists. Browing the entry_list.");
#endif

      int max_index = seen_end_idx;
      foreach(Entry e in entry_list) {
        if( e.Index > seen_end_idx ) { 
          //We may add this one:
          if( e.Data.Length + consumed_bytes <= maxbytes ) {
            //Lets add it
            TimeSpan age = DateTime.Now - e.CreatedTime;
            int age_i = (int)age.TotalSeconds;
            consumed_bytes += e.Data.Length;
            Hashtable item = new Hashtable();
            item["age"] = age_i;
            item["data"] = e.Data;
            values.Add(item);
#if DHT_DEBUG
	    Console.WriteLine("[DhtServer]: Added value to results.");
#endif
	    if (e.Index > max_index) {
	      max_index= e.Index;
	    }
          }
	    else {
            //We are all full up
	      break;
          }
        }
        else {
          //This is one we have seen, don't count it in the
          //count of seen items;
          seen++;
        }
      }
      seen_end_idx = max_index;
      /*
       * Now compute how many items remain:
       * We have either already seen them, sending them now, or not yet seen them.
       */


#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: # Total entries: " + entry_list.Count);
      Console.WriteLine("[DhtServer]: # Seen: " + seen);
      Console.WriteLine("[DhtServer]: # Values returned: " + values.Count);
#endif

      remaining_items = entry_list.Count - seen - values.Count;
    }
    else {
#if DHT_DEBUG
      Console.WriteLine("[DhtServer]: Key doesn't exist.");
#endif

      //Don't know about this key
    }
    }//End of lock

    //we have added new item: update the token
    int[] new_bounds = new int[2];
    new_bounds[0] = seen_start_idx;
    new_bounds[1] = seen_end_idx;
    //new_bounds has to be converted to a new token
    System.IO.MemoryStream ms = new System.IO.MemoryStream();
    AdrConverter.Serialize(new_bounds, ms);
    next_token = ms.ToArray();
    
    result.Add(values);
    result.Add(remaining_items);
    result.Add(next_token);
    return result;

  }
  /**
   *  delete a key from the Table
   *  @param password <hash_algo>:<base64(plain_text_password)>
   *  @throws exception if invalid password
   */
  public void Delete(byte[] key, string password)
  {
#if DHT_DEBUG
    Console.WriteLine("[DhtServer]: Delete().");
#endif    
    string hash_name = null;
    string base64_pass = null;
    if (!ValidatePasswordFormat(password, out hash_name, 
				out base64_pass)) {
      throw new Exception("Invalid password format.");
    }
    HashAlgorithm algo = null;
    if (hash_name.Equals("SHA1")) {
      algo = new SHA1CryptoServiceProvider();
    } else if  (hash_name.Equals("MD5")) {
      algo = new MD5CryptoServiceProvider();
    }
    
    byte[] bin_pass = Convert.FromBase64String(base64_pass);
    byte [] sha1_hash = algo.ComputeHash(bin_pass);
    string base64_hash = Convert.ToBase64String(sha1_hash);
    string stored_pass =  hash_name + ":" + base64_hash;
    
    
    lock(_sync ) { 
      //delete keys that have expired
      DeleteExpired();  
      
      TableKey ht_key = new TableKey(key);
      ArrayList entry_list = (ArrayList)_ht[ht_key];
      bool found = false;
      if (entry_list != null) {
#if DHT_DEBUG
	Console.WriteLine("[DhtServer]: Key exists. Browing the entry_list.");
#endif
	ArrayList to_delete = new ArrayList();

	//we will ony delete the entry which corresponds to the 
	//password provided
	//we therefore have to verify the password
	foreach(Entry e in entry_list) {
	  if (e.Password.Equals(stored_pass)) {
#if DHT_DEBUG
	    Console.WriteLine("[DhtServer]: Found a key to delete.");
#endif
	    found = true;
	    //we have found a key to delete
	    to_delete.Add(e);
	  }
	}
	foreach (Entry e in to_delete) {
	  entry_list.Remove(e);
	  //further remove the entry from the sorted list
	  DeleteFromSorted(e);  
	}
	//in case that the entry_list has shrinked to size 0, make it null
	if (entry_list.Count == 0) {
	  _ht.Remove(ht_key);
	}
      } else {
	Console.WriteLine("[DhtServer]: Key doesn't exist.");
      }
      if (!found) {
	//raise an error
	throw new Exception("Access control violation on key. Incorrect password");	
      }
    }
  }
  /** protected methods. */

  /** The method gets rid of keys that have expired. 
   *  (Assuming that _expiring_entries is sorted).
   */
  protected void DeleteExpired() {
    //scan through the list and remove entries 
    //whose expiration times have elapsed
#if DHT_DEBUG
    Console.WriteLine("[DhtServer] Getting rid of expired entries.");
#endif
    int del_count = 0;
    for (int i = 0; i < _expiring_entries.Count; i++) {
      Entry e = (Entry) _expiring_entries[i];
      DateTime end_time = e.EndTime; 
      //first entry that hasn't expired, rest all should stay
      if (end_time > DateTime.Now) 
      {
	break;
      }
      //we certainly are lookin at an entry that has expired
      //get rid of this entry
      TableKey key = new TableKey(e.Key);
      ArrayList entry_list = (ArrayList) _ht[key];
      //remove this from the entry list
      entry_list.Remove(e);
      if (entry_list.Count == 0) {
	_ht.Remove(key);
      }
      del_count++;
    }
#if DHT_DEBUG
    Console.WriteLine("[DhtServer] {0} entries stand expired.", del_count);
#endif
    if (del_count > 0) {
      _expiring_entries.RemoveRange(0, del_count);
#if DHT_DEBUG
      Console.WriteLine("[DhtServer] {0} entries deleted.", del_count);
#endif
    }
  }

  /** Add to _expiring entries. */
  protected void InsertToSorted(Entry new_entry) {
    int idx = 0;
    foreach(Entry e in _expiring_entries) {
      if (new_entry.EndTime < e.EndTime) {
	break;
      }
      idx++;
    }
#if DHT_DEBUG
    Console.WriteLine("[DhtServer] New entry ranks: {0} in sorted array.",
		      idx);
#endif
    _expiring_entries.Insert(idx, new_entry);
  }
  /** we further need a way to get rid of entries that are deleted.*/
  protected void DeleteFromSorted(Entry e) {
#if DHT_DEBUG
    Console.WriteLine("[DhtServer] Removing an entry from sorted list. ");
#endif
    _expiring_entries.Remove(e);
  }
  
  /** Methods not exposed by DHT but available only within DHT. */

  


  /** Not RPC related methods. */
  /** Invoked by local DHT object. */
  public ArrayList GetValues(byte[] key) {
    lock(_sync) {
      TableKey ht_key = new TableKey(key);  
      ArrayList entry_list = (ArrayList)_ht[ht_key];
      return entry_list;
    }
  }


  /** Get all the keys to left of some address.
   *  Note that this depends on whether the ring is stored clockwise or
   *  anti-clockwise (which are internals of connection table).
   *  
   */
  public Hashtable GetKeysToLeft(AHAddress us, AHAddress within) {
    lock(_sync) {
      Hashtable key_list = new Hashtable();
      foreach (TableKey key in _ht.Keys) {
	AHAddress target = key.GetTargetAddress();
	if (target.IsToLeftWithin(us, within)) {
	  //this is a relevant key
	  //we want to share it
	  ArrayList entry_list = (ArrayList)_ht[key];
	  key_list[key.Buffer] = entry_list.Clone();
	}
      }
      return key_list;
    }

  }

  /** Get all the keys to right of some address.
   *  Note that this depends on whether the ring is stored clockwise or
   *  anti-clockwise (which are internals of connection table).
   *  
   */
  public Hashtable GetKeysToRight(AHAddress us, AHAddress within) {
    lock(_sync) {
      Hashtable key_list = new Hashtable();
      foreach (TableKey key in _ht.Keys) {
	AHAddress target = key.GetTargetAddress();
	if (target.IsToRightWithin(us, within)) {
	  //this is a relevant key
	  //we want to share it
	  ArrayList entry_list = (ArrayList) _ht[key];
	  key_list[key.Buffer] = entry_list.Clone();

	}
      }
      return key_list;
    }
  }

  //Note: This is critical method, and allows dropping complete range of keys.
  public void AdminDelete(Hashtable key_list) {
    lock(_sync ) { 
      //delete keys that have expired
      DeleteExpired();
      foreach (byte[] k in key_list.Keys) {
	//all the values to get rid
	TableKey ht_key = new TableKey(k);
	ArrayList entry_list = (ArrayList) _ht[ht_key];
	//essentially delete all the values for that key
	if (entry_list != null) {
	  foreach(Entry e in entry_list) {
	    DeleteFromSorted(e);
	  }
	}
	_ht.Remove(ht_key);
      }
    }
  }
}
}
  
