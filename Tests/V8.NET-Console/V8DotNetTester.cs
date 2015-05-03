namespace V8.Net
{
    using System;

    /// <summary>
    /// This is a custom implementation of 'V8Function' (which is not really necessary, but done as an example).
    /// </summary>
    public class V8DotNetTesterFunction : V8Function
    {
        public override ObjectHandle Initialize(bool isConstructCall, params InternalHandle[] args)
        {
            Callback = ConstructV8DotNetTesterWrapper;

            return base.Initialize(isConstructCall, args);
        }

        public InternalHandle ConstructV8DotNetTesterWrapper(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args)
        {
            return isConstructCall ? engine.GetObject<V8DotNetTesterWrapper>(_this, true, false).Initialize(isConstructCall, args).AsInternalHandle : InternalHandle.Empty;
            // (note: V8DotNetTesterWrapper would cause an error here if derived from V8ManagedObject)
        }
    }

    /// <summary>
    /// When "new SomeType()"  is executed within JavaScript, the native V8 auto-generates objects that are not based on templates.  This means there is no way
    /// (currently) to set interceptors to support IV8Object objects; However, 'V8NativeObject' objects are supported, so I'm simply creating a custom one here.
    /// </summary>
    public class V8DotNetTesterWrapper : V8NativeObject // (I can also implement IV8NativeObject instead here)
    {
        V8DotNetTester m_tester;

        public override ObjectHandle Initialize(bool isConstructCall, params InternalHandle[] args)
        {
            m_tester = Engine.CreateObjectTemplate().CreateObject<V8DotNetTester>();
            SetProperty("tester", m_tester); // (or _Tester.Handle works also)
            return Handle;
        }
    }

    public class V8DotNetTester : V8ManagedObject
    {
        IV8Function m_myFunc;

        public override ObjectHandle Initialize(bool isConstructCall, params InternalHandle[] args)
        {
            base.Initialize(isConstructCall, args);

            Console.WriteLine("\r\nInitializing V8DotNetTester ...\r\n");

            Console.WriteLine("Creating test property 1 (adding new JSProperty directly) ...");

            var myProperty1 = new JSProperty(Engine.CreateValue("Test property 1"));
            this.Properties.Add("testProperty1", myProperty1);

            Console.WriteLine("Creating test property 2 (adding new JSProperty using the IV8ManagedObject interface) ...");

            var myProperty2 = new JSProperty(Engine.CreateValue(true));
            this["testProperty2"] = myProperty2;

            Console.WriteLine("Creating test property 3 (reusing JSProperty instance for property 1) ...");

            // Note: This effectively links property 3 to property 1, so they will both always have the same value, even if the value changes.
            this.Properties.Add("testProperty3", myProperty1); // (reuse a value)

            Console.WriteLine("Creating test property 4 (just creating a 'null' property which will be intercepted later) ...");

            this.Properties.Add("testProperty4", JSProperty.Empty);

            Console.WriteLine("Creating test property 5 (test the 'this' overload in V8ManagedObject, which will set/update property 5 without calling into V8) ...");

            this["testProperty5"] = (JSProperty)Engine.CreateValue("Test property 5");

            Console.WriteLine("Creating test property 6 (using a dynamic property) ...");

            InternalHandle strValHandle = Engine.CreateValue("Test property 6");
            this.AsDynamic.testProperty6 = strValHandle;

            Console.WriteLine("Creating test function property 1 ...");

            var funcTemplate1 = Engine.CreateFunctionTemplate("_" + GetType().Name + "_");
            m_myFunc = funcTemplate1.GetFunctionObject(TestJSFunction1);
            this.AsDynamic.testFunction1 = m_myFunc;

            Console.WriteLine("\r\n... initialization complete.");

            return Handle;
        }

        public void Execute()
        {
            Console.WriteLine("Testing pre-compiled script ...\r\n");

            Engine.Execute("var i = 0;");
            var pcScript = Engine.Compile("i = i + 1;");
            for (var i = 0; i < 100; i++)
                Engine.Execute(pcScript, true);

            Engine.ConsoleExecute("assert('Testing i==100', i, 100)", this.GetType().Name, true);

            Console.WriteLine("\r\nTesting JS function call from native side ...\r\n");

            ObjectHandle f = (ObjectHandle)Engine.ConsoleExecute("f = function(arg1) { return arg1; }");
            var fresult = f.StaticCall(Engine.CreateValue(10));
            Console.WriteLine("f(10) == " + fresult);
            if (fresult != 10)
                throw new Exception("CLR handle call to native function failed.");

            Console.WriteLine("\r\nTesting JS function call exception from native side ...\r\n");

            f = (ObjectHandle)Engine.ConsoleExecute("f = function() { return thisdoesntexist; }");
            fresult = f.StaticCall();
            Console.WriteLine("f() == " + fresult);
            if (!fresult.ToString().Contains("Error"))
                throw new Exception("Native exception error did not come through.");
            else
                Console.WriteLine("Expected exception came through - pass.\r\n");

            Console.WriteLine("\r\nPress any key to begin testing properties on 'this.tester' ...\r\n");
            Console.ReadKey();

            // ... test the non-function/object propertied ...

            Engine.ConsoleExecute("assert('Testing property testProperty1', tester.testProperty1, 'Test property 1')", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty2', tester.testProperty2, true)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty3', tester.testProperty3, tester.testProperty1)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty4', tester.testProperty4, '" + MyClassProperty4 + "')", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty5', tester.testProperty5, 'Test property 5')", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty6', tester.testProperty6, 'Test property 6')", this.GetType().Name, true);

            Console.WriteLine("\r\nAll properties initialized ok.  Testing property change ...\r\n");

            Engine.ConsoleExecute("assert('Setting testProperty2 to integer (123)', (tester.testProperty2=123), 123)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Setting testProperty2 to number (1.2)', (tester.testProperty2=1.2), 1.2)", this.GetType().Name, true);

            // ... test non-function object properties ...

            Console.WriteLine("\r\nSetting property 1 to an object, which should also set property 3 to the same object ...\r\n");

            Engine.VerboseConsoleExecute("dump(tester.testProperty1 = {x:0});", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing property testProperty1.x === testProperty3.x', tester.testProperty1.x, tester.testProperty3.x)", this.GetType().Name, true);

            // ... test function properties ...

            Engine.ConsoleExecute("assert('Testing property tester.testFunction1 with argument 100', tester.testFunction1(100), 100)", this.GetType().Name, true);

            // ... test function properties ...

            Console.WriteLine("\r\nCreating 'this.obj1' with a new instance of tester.testFunction1 and testing the expected values ...\r\n");

            Engine.VerboseConsoleExecute("obj1 = new tester.testFunction1(321);");
            Engine.ConsoleExecute("assert('Testing obj1.x', obj1.x, 321)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1.y', obj1.y, 0)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1[0]', obj1[0], 100)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1[1]', obj1[1], 100.2)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1[2]', obj1[2], '300')", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1[3] is undefined?', obj1[3] === undefined, true)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj1[4].toUTCString()', obj1[4].toUTCString(), 'Wed, 02 Jan 2013 03:04:05 GMT')", this.GetType().Name, true);

            Console.WriteLine("\r\nPress any key to test dynamic handle property access ...\r\n");
            Console.ReadKey();

            // ... get a handle to an in-script only object and test the dynamic handle access ...

            Engine.VerboseConsoleExecute("var obj = { x:0, y:0, o2:{ a:1, b:2, o3: { x:0 } } }", this.GetType().Name, true);
            dynamic handle = Engine.DynamicGlobalObject.obj;
            handle.x = 1;
            handle.y = 2;
            handle.o2.o3.x = 3;
            Engine.ConsoleExecute("assert('Testing obj.x', obj.x, 1)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj.y', obj.y, 2)", this.GetType().Name, true);
            Engine.ConsoleExecute("assert('Testing obj.o2.o3.x', obj.o2.o3.x, 3)", this.GetType().Name, true);

            Console.WriteLine("\r\nPress any key to test handle reuse ...");
            Console.WriteLine("(1000 native object handles will be created, but one V8NativeObject wrapper will be used)");
            Console.ReadKey();
            Console.Write("Running ...");
            var obj = new V8NativeObject();
            for (var i = 0; i < 1000; i++)
            {
                obj.Handle = Engine.GlobalObject.GetProperty("obj");
            }
            Console.WriteLine(" Done.");
        }

        public override InternalHandle NamedPropertyGetter(ref string propertyName)
        {
            if (propertyName == "testProperty4")
                return Engine.CreateValue(MyClassProperty4);

            return base.NamedPropertyGetter(ref propertyName);
        }

        public string MyClassProperty4 { get { return this.GetType().Name; } }

        public InternalHandle TestJSFunction1(V8Engine engine, bool isConstructCall, InternalHandle _this, params InternalHandle[] args)
        {
            // ... there can be two different returns based on the call mode! ...
            // (tip: if a new object is created and returned instead (such as V8ManagedObject or an object derived from it), then that object will be the new object (instead of "_this"))
            if (isConstructCall)
            {
                var obj = engine.GetObject(_this);
                obj.AsDynamic.x = args[0];
                ((dynamic)obj).y = 0; // (native objects in this case will always be V8NativeObject dynamic objects)
                obj.SetProperty(0, engine.CreateValue(100));
                obj.SetProperty("1", engine.CreateValue(100.2));
                obj.SetProperty("2", engine.CreateValue("300"));
                obj.SetProperty(4, engine.CreateValue(new DateTime(2013, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
                return _this;
            }
            else return args.Length > 0 ? args[0] : InternalHandle.Empty;
        }
    }
}
