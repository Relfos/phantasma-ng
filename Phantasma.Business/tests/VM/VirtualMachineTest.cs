﻿using System;
using System.Collections.Generic;
using Phantasma.Core;
using Shouldly;
using Xunit;

namespace Phantasma.Business.Tests.VM;

public class VirtualMachineTest
{
    

    public class VirtualTestMachine : VirtualMachine
    {
        public VirtualTestMachine(byte[] script, uint offset, string contextName) : base(script, offset, contextName)
        {
        }

        public override ExecutionState ExecuteInterop(string method)
        {
            return ExecutionState.Running;
        }

        public override ExecutionContext LoadContext(string contextName)
        {
            return null;
        }

        public override void DumpData(List<string> lines)
        {
            
        }

        public override ExecutionState Execute()
        {
            var result = base.Execute();
            return result;
        }

        public override string GetDumpFileName()
        {
            return base.GetDumpFileName();
        }

        public override ExecutionState ValidateOpcode(Opcode opcode)
        {
            return base.ValidateOpcode(opcode);
        }

        public ExecutionState SwitchContext(ExecutionContext context, uint instructionPointer)
        {
            var result = base.SwitchContext(context, instructionPointer);
            return result;
        }
    }

    public class TestExecutionContextDummy : ExecutionContext
    {
        public override string Name { get; }

        public TestExecutionContextDummy(string name)
        {
            this.Name = name;
        }
        
        public override ExecutionState Execute(ExecutionFrame frame, Stack<VMObject> stack)
        {
            return ExecutionState.Halt;
        }
    }
    
    public VirtualTestMachine VirtualMachine;
    
    
    [Fact]
    public void registerContext_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{0}, 0, "test");
        VirtualMachine.RegisterContext("test", new ScriptContext("testScript", new byte[]{0}, 0));
        
    }
    
    [Fact]
    public void execute_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");

        Should.Throw<VMException>(() =>
        {
            VirtualMachine.Execute();
        });
    }
    
    [Fact]
    public void pushFrame_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");
        ExecutionContext executionContext = VirtualMachine.CurrentContext;
        Should.NotThrow(() =>
        {
            VirtualMachine.PushFrame(executionContext, 0, 1);
        });
    }
    
    [Fact]
    public void peekFrame_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");

        Should.Throw<Exception>(() =>
        {
            VirtualMachine.PeekFrame();
        });
    }
    
    [Fact]
    public void setCurrentContext_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");

        Should.Throw<VMException>(() =>
        {
            VirtualMachine.SetCurrentContext(null);
        });

        Should.NotThrow(() =>
        {
            VirtualMachine.SetCurrentContext(new TestExecutionContextDummy("testContext"));
        });
    }
    
    [Fact]
    public void findContext_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");

        Should.NotThrow(() =>
        {
            VirtualMachine.FindContext("testContext").ShouldBeNull();

        });
    }
    
    [Fact]
    public void switchContext_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");
        ScriptContext scriptContext = null;
        Should.Throw<VMException>(() =>
        {
            VirtualMachine.SwitchContext(null, 0);

        });
    }
    
    [Fact]
    public void validateOpcode_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");
        ScriptContext scriptContext = new ScriptContext("test", new byte[0], 1);
        Should.NotThrow(() =>
        {
            VirtualMachine.ValidateOpcode(Opcode.POP);
        });
        
        VirtualMachine.ValidateOpcode(Opcode.POP).ShouldBe(ExecutionState.Running);
    }
    
    [Fact]
    public void getDumpFileName_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");
        ScriptContext scriptContext = new ScriptContext("test", new byte[0], 1);
        Should.NotThrow(() =>
        {
            VirtualMachine.GetDumpFileName();
        });
        
        VirtualMachine.GetDumpFileName().ShouldBe("vm.txt");
    }
    
    [Fact]
    public void expect_test()
    {
        VirtualMachine = new VirtualTestMachine(new byte[]{}, 0, "test");
        ScriptContext scriptContext = new ScriptContext("test", new byte[0], 1);
        Should.NotThrow(() =>
        {
            VirtualMachine.Expect(true, "result");
        });

        Should.Throw<VMException>(() =>
        {
            VirtualMachine.Expect(false, "resultError");

        });
        
    }

}
