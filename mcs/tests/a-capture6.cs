//
// Tests capturing of variables
//
delegate void S ();
using System;

class X {
	static int Main ()
	{
		int a = 1;
		if (a != 1)
			return 1;
		
		Console.WriteLine ("A is = " + a);
		S b= delegate {
			Console.WriteLine ("on delegate");
			a = 2;
		};
		if (a != 1)
			return 2;
		b();
		if (a != 2)
			return 3;
		Console.WriteLine ("OK");
		return 0;
	}
}
		
