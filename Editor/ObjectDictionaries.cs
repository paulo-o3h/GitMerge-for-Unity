﻿using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

namespace GitMerge
{
    /// <summary>
    /// Dictionaries that categorize the scene's objects into our objects, their objects, and temporary
    /// copies of their objects that have been instantiated while merging.
    /// </summary>
    public static class ObjectDictionaries
    {
        //This dict holds all of "our" objects
        //Needed for Reference handling
        //<fileID, Object>
        private static Dictionary<int, Object> ourObjects = new Dictionary<int, Object>();

        //This dict maps our instances of their objects
        //Whenever we instantiate a copy of "their" new object, they're both added here
        private static Dictionary<Object, Object> ourInstances = new Dictionary<Object, Object>();

        //This dict holds all of "their" GameObjects
        //Needed for scene cleaning after merge
        //<GameObject, originallyActive>
        private static Dictionary<GameObject, bool> theirObjects = new Dictionary<GameObject, bool>();

        //This dict holds all GameObjects that might or might not exist,
        //depending on the current merge state. The referenced objects are the versions that will definitely exist throughout the merge.
        //Also maps the MergeActions responsible for their existence to them.
        private static Dictionary<GameObject, MergeActionExistence> schroedingersObjects = new Dictionary<GameObject, MergeActionExistence>();


        public static void SetAsOurObjects(List<GameObject> objects)
        {
            foreach(var obj in objects)
            {
                SetAsOurObject(obj);
            }
        }

        public static void SetAsTheirObjects(List<GameObject> objects)
        {
            foreach(var obj in objects)
            {
                SetAsTheirs(obj, false);
            }
        }


        public static void SetAsOurObject(GameObject go)
        {
            AddOurObject(go);
            foreach(var c in go.GetComponents<Component>())
            {
                AddOurObject(c);
            }
        }

        public static void SetAsOurObject(Component c)
        {
            AddOurObject(c);
        }

        private static void AddOurObject(Object o)
        {
            if(o == null)
                return;

            ourObjects.Add(ObjectIDFinder.GetIdentifierFor(o), o);
        }

        public static void RemoveOurObject(GameObject go)
        {
            foreach(var c in go.GetComponents<Component>())
            {
                RemoveOurSingleObject(c);
            }
            RemoveOurSingleObject(go);
        }

        public static void RemoveOurObject(Component c)
        {
            RemoveOurSingleObject(c);
        }

        private static void RemoveOurSingleObject(Object o)
        {
            if(o == null)
                return;

            ourObjects.Remove(ObjectIDFinder.GetIdentifierFor(o));
        }

        public static Object GetOurObject(int id)
        {
            Object result = null;
            ourObjects.TryGetValue(id, out result);
            return result;
        }

        /// <summary>
        /// Returns:
        /// * the given object if it is "ours"
        /// * "our" counterpart of obj if it is "theirs"
        /// * null if the object is deleted for some reason
        /// The returned object can be an instance of "their" object temporarily added for the merge
        /// </summary>
        /// <param name="obj">the original object</param>
        /// <returns>the counterpart of the object in "our" version</returns>
        public static Object GetOurCounterpartFor(Object obj)
        {
            var result = obj;
            if(IsTheirs(obj))
            {
                result = GetOurObject(ObjectIDFinder.GetIdentifierFor(obj));
                if(!result)
                {
                    result = GetOurInstanceOfCopy(obj);
                }
            }
            return result;
        }

        public static void Clear()
        {
            ourObjects.Clear();
            theirObjects.Clear();
            ourInstances.Clear();
            schroedingersObjects.Clear();
        }

        /// <summary>
        /// Mark o as an instance of theirs
        /// </summary>
        public static void SetAsCopy(GameObject o, GameObject theirs)
        {
            ourInstances.Add(theirs, o);
            var instanceComponents = o.GetComponents<Component>();
            var theirComponents = theirs.GetComponents<Component>();
            for(int i = 0; i < instanceComponents.Length; ++i)
            {
                SetAsCopy(instanceComponents[i], theirComponents[i]);
            }
        }

        public static void SetAsCopy(Component c, Component theirs)
        {
            if(c == null)
                return;

            ourInstances.Add(theirs, c);
        }

        public static void RemoveCopyOf(GameObject theirs)
        {
            ourInstances.Remove(theirs);
            foreach(var c in theirs.GetComponents<Component>())
            {
                if(c != null)
                {
                    ourInstances.Remove(c);
                }
            }
        }

        public static void RemoveCopyOf(Component theirs)
        {
            ourInstances.Remove(theirs);
        }

        /// <summary>
        /// Returns:
        /// * the given object if it is "ours"
        /// * "our" instance of obj if it is "theirs"
        /// * null if there is no such instance
        /// </summary>
        /// <param name="obj">the original object</param>
        /// <returns>the instance of the original object</returns>
        public static Object GetOurInstanceOfCopy(Object obj)
        {
            var result = obj;
            if(IsTheirs(obj))
            {
                ourInstances.TryGetValue(obj, out result);
                //Debug.LogFormat("It's their object {0} with result {1}", obj, result);
                //if(result == null)
                //{
                //    Debug.LogFormat("No instance of {0} found in ourInstances", obj);
                //}
            }
            return result;
        }

        private static bool IsTheirs(Object obj)
        {
            var go = obj as GameObject;
            if(go)
            {
                return theirObjects.ContainsKey(go);
            }
            var c = obj as Component;
            if(c)
            {
                return theirObjects.ContainsKey(c.gameObject);
            }
            return false;
        }

        public static void SetAsTheirs(GameObject go, bool active)
        {
            if(!theirObjects.ContainsKey(go))
            {
                theirObjects.Add(go, go.activeSelf);
            }
            go.SetActiveForMerging(false);
        }

        /// <summary>
        /// Copy an object that has been disabled and hidden for merging into the scene,
        /// enable and unhide the copy.
        /// </summary>
        /// <param name="go">The original GameObject.</param>
        /// <returns>The copy GameObject.</returns>
        public static GameObject InstantiateFromMerging(GameObject go)
        {
            var copy = Object.Instantiate(go) as GameObject;

            //Destroy children
            foreach(Transform t in copy.GetComponent<Transform>())
            {
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            bool wasActive;
            if(!theirObjects.TryGetValue(go, out wasActive))
            {
                wasActive = go.activeSelf;
            }

            //Apply some special properties of the GameObject
            copy.SetActive(wasActive);
            copy.hideFlags = HideFlags.None;
            copy.name = go.name;
            SetParentInOurs(copy, go);
            SetAllChildren(copy, go); //doing this everytime even if not needed always
            // perhaps find a check if the parent was already found
            return copy;
        }

        private static void SetParentInOurs(GameObject copy, GameObject go)
        {
            //Debug.LogFormat("SetParentInOur() called for {0} with their parent being {1}",
            //    copy,
            //    go.transform.parent
            //);
            var parentCounterpart = GetOurCounterpartFor(go.transform.parent) as Transform;
            //Debug.LogFormat("Setting the parent of {0} to {1} a counterpart of {2}",
                //copy,
                //parentCounterpart,
                //go.transform.parent
                //);
            // Children don't always find their parents :(
            // If the our counterpart to their object hasn't been created yet...
            copy.transform.SetParent(
                parentCounterpart,
                true);
        }

        private static void SetAllChildren(GameObject copy, GameObject go)
        {
            //Debug.LogFormat("Looking for children of {0} found {1}",
                //go,
                //go.transform.childCount);
            for(int i=0; i<go.transform.childCount; i++)
            {
                var theirChild = go.transform.GetChild(i);
                var ourChild = GetOurCounterpartFor(theirChild) as Transform;
                if(ourChild)
                {
                    ourChild.SetParent(copy.transform);
                    //Debug.LogFormat("Setting parent of {0} to {1}",
                        //ourChild,
                        //copy
                        //);
                }
                //else
                //{
                //    Debug.LogFormat("No corresponding child for their child {0}", theirChild);
                //}
            }

        }

        public static void DestroyTheirObjects()
        {
            foreach(var obj in theirObjects.Keys)
            {
                Object.DestroyImmediate(obj);
            }
            theirObjects.Clear();
        }

        public static void AddToSchroedingersObjects(GameObject go, MergeActionExistence mergeAction)
        {
            schroedingersObjects.Add(go, mergeAction);
        }

        public static void EnsureExistence(GameObject go)
        {
            schroedingersObjects[go].EnsureExistence();
        }
    }
}