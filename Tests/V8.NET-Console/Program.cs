namespace V8.Net
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using Timer = System.Timers.Timer;

    public class SamplePointFunctionTemplate : FunctionTemplate
    {
        public SamplePointFunctionTemplate() { }

        protected override void OnInitialized()
        {
            base.OnInitialized();
        }
    }


    public class Program
    {
        static V8Engine s_jsServer;

        static Timer s_titleUpdateTimer;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                Console.Write(Environment.NewLine + "Creating a V8Engine instance ...");
                s_jsServer = new V8Engine();
                //s_jsServer.SetFlags("--harmony");
                Console.WriteLine(" Done!");

                Console.Write("Testing marshalling compatibility...");
                s_jsServer.RunMarshallingTests();
                Console.WriteLine(" Pass!");

                s_titleUpdateTimer = new Timer(500)
                {
                    AutoReset = true
                };

                s_titleUpdateTimer.Elapsed += (o, e) =>
                {
                    if (!s_jsServer.IsDisposed)
                        Console.Title = "V8.Net Console - " + (IntPtr.Size == 4 ? "32-bit" : "64-bit") + " mode (Handles: " + s_jsServer.TotalHandles
                            + " / Pending Native GC: " + s_jsServer.TotalHandlesPendingDisposal
                            + " / Cached: " + s_jsServer.TotalHandlesCached
                            + " / In Use: " + (s_jsServer.TotalHandles - s_jsServer.TotalHandlesCached) + ")";
                    else
                        Console.Title = "V8.Net Console - Shutting down...";
                };
                s_titleUpdateTimer.Start();

                {
                    Console.WriteLine(Environment.NewLine + "Creating some global CLR types ...");

                    // (Note: It's not required to explicitly register a type, but it is recommended for more control.)

                    s_jsServer.RegisterType(typeof(Object), "Object", true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Type), "Type", true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(String), "String", true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Boolean), "Boolean", true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Array), "Array", true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(ArrayList), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(char), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(int), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Int16), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Int32), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Int64), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(UInt16), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(UInt32), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(UInt64), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(Enumerable), null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.RegisterType(typeof(File), null, true, ScriptMemberSecurity.Locked);

                    ObjectHandle hSystem = s_jsServer.CreateObject();
                    s_jsServer.DynamicGlobalObject.System = hSystem;
                    hSystem.SetProperty(typeof(Object)); // (Note: No optional parameters used, so this will simply lookup and apply the existing registered type details above.)
                    hSystem.SetProperty(typeof(String));
                    hSystem.SetProperty(typeof(Boolean));
                    hSystem.SetProperty(typeof(Array));
                    s_jsServer.GlobalObject.SetProperty(typeof(Type));
                    s_jsServer.GlobalObject.SetProperty(typeof(ArrayList));
                    s_jsServer.GlobalObject.SetProperty(typeof(char));
                    s_jsServer.GlobalObject.SetProperty(typeof(int));
                    s_jsServer.GlobalObject.SetProperty(typeof(Int16));
                    s_jsServer.GlobalObject.SetProperty(typeof(Int32));
                    s_jsServer.GlobalObject.SetProperty(typeof(Int64));
                    s_jsServer.GlobalObject.SetProperty(typeof(UInt16));
                    s_jsServer.GlobalObject.SetProperty(typeof(UInt32));
                    s_jsServer.GlobalObject.SetProperty(typeof(UInt64));
                    s_jsServer.GlobalObject.SetProperty(typeof(Enumerable));
                    s_jsServer.GlobalObject.SetProperty(typeof(Environment));
                    s_jsServer.GlobalObject.SetProperty(typeof(File));

                    s_jsServer.GlobalObject.SetProperty(typeof(Uri), V8PropertyAttributes.Locked, null, true, ScriptMemberSecurity.Locked); // (Note: Not yet registered, but will auto register!)
                    s_jsServer.GlobalObject.SetProperty("uri", new Uri("http://www.example.com"));

                    s_jsServer.GlobalObject.SetProperty(typeof(GenericTest<int, string>), V8PropertyAttributes.Locked, null, true, ScriptMemberSecurity.Locked);
                    s_jsServer.GlobalObject.SetProperty(typeof(GenericTest<string, int>), V8PropertyAttributes.Locked, null, true, ScriptMemberSecurity.Locked);

                    Console.WriteLine(Environment.NewLine + "Creating a global 'dump(obj)' function to dump properties of objects (one level only) ...");
                    s_jsServer.ConsoleExecute(@"dump = function(o) { var s=''; if (typeof(o)=='undefined') return 'undefined';"
                        + @" if (typeof o.valueOf=='undefined') return ""'valueOf()' is missing on '""+(typeof o)+""' - if you are inheriting from V8ManagedObject, make sure you are not blocking the property."";"
                        + @" if (typeof o.toString=='undefined') return ""'toString()' is missing on '""+o.valueOf()+""' - if you are inheriting from V8ManagedObject, make sure you are not blocking the property."";"
                        + @" for (var p in o) {var ov='', pv=''; try{ov=o.valueOf();}catch(e){ov='{error: '+e.message+': '+dump(o)+'}';} try{pv=o[p];}catch(e){pv=e.message;} s+='* '+ov+'.'+p+' = ('+pv+')\r\n'; } return s; }");

                    Console.WriteLine(Environment.NewLine + "Creating a global 'assert(msg, a,b)' function for property value assertion ...");
                    s_jsServer.ConsoleExecute(@"assert = function(msg,a,b) { msg += ' ('+a+'==='+b+'?)'; if (a === b) return msg+' ... Ok.'; else throw msg+' ... Failed!'; }");

                    Console.WriteLine(Environment.NewLine + "Creating a global 'Console' object ...");
                    s_jsServer.GlobalObject.SetProperty(typeof(Console), V8PropertyAttributes.Locked, null, true, ScriptMemberSecurity.Locked);
                    //??_JSServer.CreateObject<JS_Console>();

                    Console.WriteLine(Environment.NewLine + "Creating a new global type 'TestEnum' ...");
                    s_jsServer.GlobalObject.SetProperty(typeof(TestEnum), V8PropertyAttributes.Locked, null, true, ScriptMemberSecurity.Locked);

                    Console.WriteLine(Environment.NewLine + "Creating a new global type 'SealedObject' as 'Sealed_Object' ...");
                    Console.WriteLine("(represents a 3rd-party inaccessible V8.NET object.)");
                    s_jsServer.GlobalObject.SetProperty(typeof(SealedObject), V8PropertyAttributes.Locked, null, true, null);

                    Console.WriteLine(Environment.NewLine + "Creating a new wrapped and locked object 'sealedObject' ...");
                    s_jsServer.GlobalObject.SetProperty("sealedObject", new SealedObject(null, null), null, true, ScriptMemberSecurity.Locked);

                    Console.WriteLine(Environment.NewLine + "Dumping global properties ...");
                    s_jsServer.VerboseConsoleExecute(@"dump(this)");

                    Console.WriteLine(Environment.NewLine + "Here is a contrived example of calling and passing CLR methods/types ...");
                    s_jsServer.VerboseConsoleExecute(@"r = Enumerable.Range(1,Int32('10'));");
                    s_jsServer.VerboseConsoleExecute(@"a = System.String.Join$1([Int32], ', ', r);");

                    Console.WriteLine(Environment.NewLine + "Example of changing 'System.String.Empty' member security attributes to 'NoAccess'...");
                    s_jsServer.GetTypeBinder(typeof(String)).ChangeMemberSecurity("Empty", ScriptMemberSecurity.NoAcccess);
                    s_jsServer.VerboseConsoleExecute(@"System.String.Empty;");
                    Console.WriteLine("(Note: Access denied is only for static types - bound instances are more dynamic, and will hide properties instead [name/index interceptors are not available on V8 Function objects])");

                    Console.WriteLine(Environment.NewLine + "Finally, how to view method signatures...");
                    s_jsServer.VerboseConsoleExecute(@"dump(System.String.Join);");

                    var funcTemp = s_jsServer.CreateFunctionTemplate<SamplePointFunctionTemplate>("SamplePointFunctionTemplate");
                }

                Console.WriteLine(Environment.NewLine + @"Ready - just enter script to execute. Type '\' or '\help' for a list of console specific commands.");

                while (true)
                {
                    try
                    {
                        Console.Write(Environment.NewLine + "> ");

                        var input = Console.ReadLine();
                        var lcInput = input.Trim().ToLower();

                        if (lcInput == @"\help" || lcInput == @"\")
                        {
                            Console.WriteLine(@"Special console commands (all commands are triggered via a preceding '\' character so as not to confuse it with script code):");
                            Console.WriteLine(@"\cls - Clears the screen.");
                            Console.WriteLine(@"\test - Starts the test process.");
                            Console.WriteLine(@"\gc - Triggers garbage collection (for testing purposes).");
                            Console.WriteLine(@"\v8gc - Triggers garbage collection in V8 (for testing purposes).");
                            Console.WriteLine(@"\gctest - Runs a simple GC test against V8.NET and the native V8 engine.");
                            Console.WriteLine(@"\speedtest - Runs a simple test script to test V8.NET performance with the V8 engine.");
                            Console.WriteLine(@"\mtest - Runs a simple test script to test V8.NET integration/marshalling compatibility with the V8 engine on your system.");
                            Console.WriteLine(@"\newenginetest - Creates 3 new engines (each time) and runs simple expressions in each one (note: new engines are never removed once created).");
                            Console.WriteLine(@"\exit - Exists the console.");
                        }
                        else if (lcInput == @"\cls")
                            Console.Clear();
                        else if (lcInput == @"\test")
                        {
                            try
                            {
                                /* This command will serve as a means to run fast tests against various aspects of V8.NET from the JavaScript side.
                                 * This is preferred over unit tests because 1. it takes a bit of time for the engine to initialize, 2. internal feedback
                                 * can be sent to the console from the environment, and 3. serves as a nice implementation example.
                                 * The unit testing project will serve to test basic engine instantiation and solo utility classes.
                                 * In the future, the following testing process may be redesigned to be runnable in both unit tests and console apps.
                                 */

                                Console.WriteLine("\r\n===============================================================================");
                                Console.WriteLine("Setting up the test environment ...\r\n");

                                {
                                    // ... create a function template in order to generate our object! ...
                                    // (note: this is not using ObjectTemplate because the native V8 does not support class names for those objects [class names are object type names])

                                    Console.Write("\r\nCreating a FunctionTemplate instance ...");
                                    var funcTemplate = s_jsServer.CreateFunctionTemplate(typeof(V8DotNetTesterWrapper).Name);
                                    Console.WriteLine(" Ok.");

                                    // ... use the template to generate our object ...

                                    Console.Write("\r\nRegistering the custom V8DotNetTester function object ...");
                                    var testerFunc = funcTemplate.GetFunctionObject<V8DotNetTesterFunction>();
                                    s_jsServer.DynamicGlobalObject.V8DotNetTesterWrapper = testerFunc;
                                    Console.WriteLine(" Ok.  'V8DotNetTester' is now a type [Function] in the global scope.");

                                    Console.Write("\r\nCreating a V8DotNetTester instance from within JavaScript ...");
                                    // (note: Once 'V8DotNetTester' is constructed, the 'Initialize()' override will be called immediately before returning,
                                    // but you can return "engine.GetObject<V8DotNetTester>(_this.Handle, true, false)" to prevent it.)
                                    s_jsServer.VerboseConsoleExecute("testWrapper = new V8DotNetTesterWrapper();");
                                    s_jsServer.VerboseConsoleExecute("tester = testWrapper.tester;");
                                    Console.WriteLine(" Ok.");

                                    // ... Ok, the object exists, BUT, it is STILL not yet part of the global object, so we add it next ...

                                    Console.Write("\r\nRetrieving the 'tester' property on the global object for the V8DotNetTester instance ...");
                                    var handle = s_jsServer.GlobalObject.GetProperty("tester");
                                    var tester = (V8DotNetTester)s_jsServer.DynamicGlobalObject.tester;
                                    Console.WriteLine(" Ok.");

                                    Console.WriteLine("\r\n===============================================================================");
                                    Console.WriteLine("Dumping global properties ...\r\n");

                                    s_jsServer.VerboseConsoleExecute("dump(this)");

                                    Console.WriteLine("\r\n===============================================================================");
                                    Console.WriteLine("Dumping tester properties ...\r\n");

                                    s_jsServer.VerboseConsoleExecute("dump(tester)");

                                    // ... example of adding a functions via script (note: V8Engine.GlobalObject.Properties will have 'Test' set) ...

                                    Console.WriteLine("\r\n===============================================================================");
                                    Console.WriteLine("Ready to run the tester, press any key to proceed ...\r\n");
                                    Console.ReadKey();

                                    tester.Execute();

                                    Console.WriteLine("\r\nReleasing managed tester object ...\r\n");
                                    tester.Handle.ReleaseManagedObject();
                                }

                                Console.WriteLine("\r\n===============================================================================\r\n");
                                Console.WriteLine("Test completed successfully! Any errors would have interrupted execution.");
                                Console.WriteLine("Note: The 'dump(obj)' function is available to use for manual inspection.");
                                Console.WriteLine("Press any key to dump the global properties ...");
                                Console.ReadKey();
                                s_jsServer.VerboseConsoleExecute("dump(this);");
                            }
                            catch
                            {
                                Console.WriteLine("\r\nTest failed.\r\n");
                                throw;
                            }
                        }
                        else if (lcInput == @"\gc")
                        {
                            Console.Write("\r\nForcing garbage collection ... ");
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            Console.WriteLine("Done.\r\n");
                        }
                        else if (lcInput == @"\v8gc")
                        {
                            Console.Write("\r\nForcing V8 garbage collection ... ");
                            s_jsServer.ForceV8GarbageCollection();
                            Console.WriteLine("Done.\r\n");
                        }
                        else if (lcInput == @"\gctest")
                        {
                            Console.WriteLine("\r\nTesting garbage collection ... ");

                            V8NativeObject tempObj;
                            InternalHandle internalHandle = InternalHandle.Empty;
                            int i;

                            {
                                Console.WriteLine("Setting 'this.tempObj' to a new managed object ...");

                                tempObj = s_jsServer.CreateObject<V8NativeObject>();
                                internalHandle = tempObj.Handle;
                                Handle testHandle = internalHandle;
                                s_jsServer.DynamicGlobalObject.tempObj = tempObj;

                                // ... because we have a strong reference to the handle, the managed and native objects are safe; however,
                                // this block has the only strong reference, so once the reference goes out of scope, the managed GC will attempt to
                                // collect it, which will mark the handle as ready for collection (but it will not be destroyed just yet) ...

                                Console.WriteLine("Clearing managed references and running the garbage collector ...");
                                testHandle = null;
                            }

                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            // (we wait for the 'testHandle' handle object to be collected, which will dispose the handle)
                            // (note: we do not call 'Set()' on 'internalHandle' because the "Handle" type takes care of the disposal)

                            for (i = 0; i < 3000 && internalHandle.ReferenceCount > 1; i++)
                                Thread.Sleep(1); // (just wait for the worker)

                            if (internalHandle.ReferenceCount > 1)
                                throw new Exception("Handle is still not ready for GC ... something is wrong.");

                            Console.WriteLine("Success! The managed handle instance is pending disposal.");
                            Console.WriteLine("Clearing the handle object reference next ...");

                            // ... because we still have a reference to 'tempObj' at this point, the managed and native objects are safe (and the handle
                            // above!); however, this block has the only strong reference keeping everything alive (including the handle), so once the
                            // reference goes out of scope, the managed GC will collect it, which will mark the managed object as ready for collection.
                            // Once both the managed object and handle are marked, this in turn marks the native handle as weak. When the native V8
                            // engine's garbage collector is ready to dispose of the handle, as call back is triggered and the native object and
                            // handles will finally be removed ...

                            tempObj = null;

                            Console.WriteLine("Forcing CLR garbage collection ... ");
                            GC.Collect();
                            GC.WaitForPendingFinalizers();

                            Console.WriteLine("Waiting on the worker to make the object weak on the native V8 side ... ");

                            for (i = 0; i < 6000 && !internalHandle.IsNativelyWeak; i++)
                                Thread.Sleep(1);

                            if (!internalHandle.IsNativelyWeak)
                                throw new Exception("Object is not weak yet ... something is wrong.");


                            Console.WriteLine("Forcing V8 garbage collection ... ");
                            s_jsServer.DynamicGlobalObject.tempObj = null;
                            for (i = 0; i < 3000 && !internalHandle.IsDisposed; i++)
                            {
                                s_jsServer.ForceV8GarbageCollection();
                                Thread.Sleep(1);
                            }

                            Console.WriteLine("Looking for object ...");

                            if (!internalHandle.IsDisposed) throw new Exception("Managed object was not garbage collected.");
                            // (note: this call is only valid as long as no more objects are created before this point)
                            Console.WriteLine("Success! The managed V8NativeObject instance is disposed.");
                            Console.WriteLine("\r\nDone.\r\n");
                        }
                        else if (lcInput == @"\speedtest")
                        {
                            var timer = new Stopwatch();
                            long startTime, elapsed;
                            long count;
                            double result1, result2, result3, result4;

                            Console.WriteLine(Environment.NewLine + "Running the speed tests ... ");

                            timer.Start();

                            //??Console.WriteLine(Environment.NewLine + "Running the property access speed tests ... ");
                            Console.WriteLine("(Note: 'V8NativeObject' objects are always faster than using the 'V8ManagedObject' objects because native objects store values within the V8 engine and managed objects store theirs on the .NET side.)");

                            count = 200000000;

                            Console.WriteLine("\r\nTesting global property write speed ... ");
                            startTime = timer.ElapsedMilliseconds;
                            s_jsServer.Execute("o={i:0}; for (o.i=0; o.i<" + count + "; o.i++) n = 0;"); // (o={i:0}; is used in case the global object is managed, which will greatly slow down the loop)
                            elapsed = timer.ElapsedMilliseconds - startTime;
                            result1 = (double)elapsed / count;
                            Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result1.ToString("0.0#########") + " ms each pass.");

                            Console.WriteLine("\r\nTesting global property read speed ... ");
                            startTime = timer.ElapsedMilliseconds;
                            s_jsServer.Execute("for (o.i=0; o.i<" + count + "; o.i++) n;");
                            elapsed = timer.ElapsedMilliseconds - startTime;
                            result2 = (double)elapsed / count;
                            Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result2.ToString("0.0#########") + " ms each pass.");

                            count = 200000;

                            Console.WriteLine("\r\nTesting property write speed on a managed object (with interceptors) ... ");
                            s_jsServer.DynamicGlobalObject.mo = s_jsServer.CreateObjectTemplate().CreateObject();
                            startTime = timer.ElapsedMilliseconds;
                            s_jsServer.Execute("o={i:0}; for (o.i=0; o.i<" + count + "; o.i++) mo.n = 0;");
                            elapsed = timer.ElapsedMilliseconds - startTime;
                            result3 = (double)elapsed / count;
                            Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result3.ToString("0.0#########") + " ms each pass.");

                            Console.WriteLine("\r\nTesting property read speed on a managed object (with interceptors) ... ");
                            startTime = timer.ElapsedMilliseconds;
                            s_jsServer.Execute("for (o.i=0; o.i<" + count + "; o.i++) mo.n;");
                            elapsed = timer.ElapsedMilliseconds - startTime;
                            result4 = (double)elapsed / count;
                            Console.WriteLine(count + " loops @ " + elapsed + "ms total = " + result4.ToString("0.0#########") + " ms each pass.");

                            Console.WriteLine("\r\nUpdating native properties is {0:N2}x faster than managed ones.", result3 / result1);
                            Console.WriteLine("\r\nReading native properties is {0:N2}x faster than managed ones.", result4 / result2);

                            Console.WriteLine("\r\nDone.\r\n");
                        }
                        else if (lcInput == @"\exit")
                        {
                            Console.WriteLine("User requested exit, disposing the engine instance ...");
                            s_jsServer.Dispose();
                            Console.WriteLine("Engine disposed successfully. Press any key to continue ...");
                            Console.ReadKey();
                            Console.WriteLine("Goodbye. :)");
                            break;
                        }
                        else if (lcInput == @"\mtest")
                        {
                            Console.WriteLine("Loading and marshalling native structs with test data ...");

                            s_jsServer.RunMarshallingTests();

                            Console.WriteLine("Success! The marshalling between native and managed side is working as expected.");
                        }
                        else if (lcInput == @"\newenginetest")
                        {
                            Console.WriteLine("Creating 3 more engines ...");

                            var engine1 = new V8Engine();
                            var engine2 = new V8Engine();
                            var engine3 = new V8Engine();

                            Console.WriteLine("Running test expressions ...");

                            var resultHandle = engine1.Execute("1 + 2");
                            var result = resultHandle.AsInt32;
                            Console.WriteLine("Engine 1: 1+2=" + result);
                            resultHandle.Dispose();

                            resultHandle = engine2.Execute("2+3");
                            result = resultHandle.AsInt32;
                            Console.WriteLine("Engine 2: 2+3=" + result);
                            resultHandle.Dispose();

                            resultHandle = engine3.Execute("3 + 4");
                            result = resultHandle.AsInt32;
                            Console.WriteLine("Engine 3: 3+4=" + result);
                            resultHandle.Dispose();

                            Console.WriteLine("Done.");
                        }
                        else if (lcInput == @"\memleaktest")
                        {
                            var script = @"
for (var i=0; i < 1000; i++) {
// if the loop is empty no memory leak occurs.
// if any of the following 3 method calls are uncommented then a bad memory leak occurs.
//SomeMethods.StaticDoNothing();
//shared.StaticDoNothing();
shared.InstanceDoNothing();
}
";
                            s_jsServer.GlobalObject.SetProperty(typeof(SomeMethods), V8PropertyAttributes.None, null, true, ScriptMemberSecurity.ReadWrite);
                            var sm = new SomeMethods();
                            s_jsServer.GlobalObject.SetProperty("shared", sm, null, true, null);
                            var hScript = s_jsServer.Compile(script, null, true);
                            var i = 0;
                            try
                            {
                                while (true)
                                {
                                    // putting a using statement on the returned handle stops the memory leak when running just the for loop.
                                    // using a compiled script seems to reduce garbage collection, but does not affect the memory leak
                                    using (var h = s_jsServer.Execute(hScript, true))
                                    {
                                    } // end using handle returned by execute
                                    s_jsServer.DoIdleNotification();
                                    Thread.Sleep(1);
                                    i++;
                                    if (i % 1000 == 0)
                                    {
                                        GC.Collect();
                                        GC.WaitForPendingFinalizers();
                                        s_jsServer.ForceV8GarbageCollection();
                                        i = 0;
                                    }
                                } // end infinite loop
                            }
                            catch (OutOfMemoryException ex)
                            {
                                Console.WriteLine(ex);
                                Console.ReadKey();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                Console.ReadKey();
                            }
                            //?catch
                            //{
                            //    Console.WriteLine("We caught something");
                            //    Console.ReadKey();
                            //}
                        }
                        else if (lcInput.StartsWith(@"\"))
                        {
                            Console.WriteLine(@"Invalid console command. Type '\help' to see available commands.");
                        }
                        else
                        {
                            Console.WriteLine();

                            try
                            {
                                var result = s_jsServer.Execute(input, "V8.NET Console");
                                Console.WriteLine(result.AsString);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine();
                                Console.WriteLine();
                                Console.WriteLine(ex.GetFullErrorMessage());
                                Console.WriteLine();
                                Console.WriteLine("Error!  Press any key to continue ...");
                                Console.ReadKey();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine(ex.GetFullErrorMessage());
                        Console.WriteLine();
                        Console.WriteLine("Error!  Press any key to continue ...");
                        Console.ReadKey();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine(Exceptions.GetFullErrorMessage(ex));
                Console.WriteLine();
                Console.WriteLine("Error!  Press any key to exit ...");
                Console.ReadKey();
            }

            if (s_titleUpdateTimer != null)
                s_titleUpdateTimer.Dispose();
        }

        static void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
        }
    }


    public class SomeMethods
    {
        public static void StaticDoNothing()
        {
        }
        public void InstanceDoNothing()
        {
        }
    }

    public enum TestEnum
    {
        A = 1,
        B = 2
    }

    public class GenericTest<T, T2>
    {
        public T Value;
        public T2 Value2;
    }

    [ScriptObject("Sealed_Object", ScriptMemberSecurity.Permanent)]
    public sealed class SealedObject : IV8NativeObject
    {
        public static TestEnum _StaticField = TestEnum.A;
        public static TestEnum StaticField { get { return _StaticField; } }

        int m_value;
        public int this[int index] { get { return m_value; } set { m_value = value; } }

        public Uri Uri;
        public void SetUri(Uri uri) { Uri = uri; }

        public int? FieldA = 1;
        public string FieldB = "!!!";
        public int? PropA { get { return FieldA; } }
        public string PropB { get { return FieldB; } }
        public InternalHandle H1 = InternalHandle.Empty;
        public Handle H2 = Handle.Empty;
        public V8Engine Engine;

        public SealedObject(InternalHandle h1, InternalHandle h2)
        {
            H1.Set(h1);
            H2 = h2;
        }

        public string Test(int a, string b) { FieldA = a; FieldB = b; return a + "_" + b; }
        public InternalHandle SetHandle1(InternalHandle h) { return H1.Set(h); }
        public Handle SetHandle2(Handle h) { return H2.Set(h); }
        public InternalHandle SetEngine(V8Engine engine) { Engine = engine; return Engine.GlobalObject; }

        public void Test<TT2, TT>(TT2 a, string b) { }
        public void Test<TT2, TT>(TT a, string b) { }

        public string Test(string b, int a = 1) { FieldA = a; FieldB = b; return b + "_" + a; }

        public void Test(params string[] s) { Console.WriteLine(string.Join("", s)); }

        public object[] TestD<T1, T2>() { return new object[2] { typeof(T1), typeof(T2) }; }
        public int[] TestE(int i1, int i2) { return new int[2] { i1, i2 }; }

        public void Initialize(V8NativeObject owner, bool isConstructCall, params InternalHandle[] args)
        {
        }

        public void Dispose()
        {
        }
    }


    //!!public class __UsageExamplesScratchArea__ // (just here to help with writing examples for documentation, etc.)
    //{
    //    public void Examples()
    //    {
    //        var v8Engine = new V8Engine();

    //        v8Engine.WithContextScope = () =>
    //        {
    //            // Example: Creating an instance.

    //            var result = v8Engine.Execute("/* Some JavaScript Code Here */", "My V8.NET Console");
    //            Console.WriteLine(result.AsString);
    //            Console.WriteLine("Press any key to continue ...");
    //            Console.ReadKey();

    //            Handle handle = v8Engine.CreateInteger(0);
    //            var handle = (Handle)v8Engine.CreateInteger(0);

    //            var handle = v8Engine.CreateInteger(0);
    //            // (... do something with it ...)
    //            handle.Dispose();

    //            // ... OR ...

    //            using (var handle = v8Engine.CreateInteger(0))
    //            {
    //                // (... do something with it ...)
    //            }

    //            // ... OR ...

    //            InternalHandle handle = InternalHandle.Empty;
    //            try
    //            {
    //                handle = v8Engine.CreateInteger(0);
    //                // (... do something with it ...)
    //            }
    //            finally { handle.Dispose(); }

    //            handle.Set(anotherHandle);
    //            // ... OR ...
    //            var handle = anotherHandle.Clone(); // (note: this is only valid when initializing a variable)

    //            var handle = v8Engine.CreateInteger(0);
    //            var handle2 = handle;

    //            handle.Set(anotherHandle.Clone());

    //            // Example: Setting global properties.

    //        };
    //    }
    //}
}
