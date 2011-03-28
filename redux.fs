// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts

#light


// dependencies /////////////////////////////////////////////

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// command line args /////////////////////////////////////////////

let ( api, email, password ) = match System.Environment.GetCommandLineArgs() with
                               | [| _; blog; email; password |] -> 
                                    ( ("http://" + blog + ".tumblr.com/api"), email, password )
                               | _ -> 
                                    failwithf "Usage: mono exe BLOG EMAIL PASSWORD"


// fetch a URL /////////////////////////////////////////////
(*
   With async, is it more reasonable to just let things fail early, here,
   rather than continuing? 
   
   TODO: How can I see both the error and its type?

   async {
      try
         ...
      with
         | :? System.UriFormatException -> return ""
         | :? System.Net.WebException   -> return ""
   }
*)


(*
LATEST ERROR:

   > testPostReblogging 6666;;
   Got 6666 to 6666 of 9071
   -> id: 371932796, rkey: CWTormwx, 2/5/2010 12:00:00 AM
      http://highheel.tumblr.com/photo/1280/371932796/1/tumblr_kvhebhHzXt1qa355f
   request.Method: POST
   System.Net.WebException: The remote server returned an error: (403) Forbidden.
     at System.Net.HttpWebRequest.CheckFinalStatus (System.Net.WebAsyncResult result) [0x00000] in <filename unknown>:0 
     at System.Net.HttpWebRequest.SetResponseData (System.Net.WebConnectionData data) [0x00000] in <filename unknown>:0 
   Stopped due to error

   Wireshark reveals that this is sending an HTTP GET.  I am going crazy.
*)

let getDocRaw (url:string) : string = 

   (Async.RunSynchronously(async {
      let req        = WebRequest.Create(url, Timeout=5)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()

(*
      reader.Close()
      response.Close()
      req.Abort()
*)

      return output
   }))


let postDocRaw (url:string) (data: string) : string =
      let data' : byte[] = System.Text.Encoding.ASCII.GetBytes(data);

      let request = WebRequest.Create(url)
      request.Method        <- "POST"
      request.ContentType   <- "application/x-www-form-urlencoded"
      request.ContentLength <- (int64) data'.Length

      use wstream = request.GetRequestStream() 
      wstream.Write(data',0, (data'.Length))
      wstream.Flush()
      wstream.Close()
(*
      use writer = new StreamWriter (wstream) 
      writer.Write(data')
      writer.Flush()
      writer.Close()
*)

      printfn "request.Method: %s" request.Method

      let response  = request.GetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()

      reader.Close()
      response.Close()
      request.Abort()

      output


// simple queries /////////////////////////////////////////////

// xml out
let readPosts ((start,num): int*int) : string      = getDocRaw <| api + "/read" +
                                                         "?start=" + (sprintf "%d" start) + 
                                                         "&num="   + (sprintf "%d" num) +
                                                         "&type="  + "photo"

// status out
let deletePost (id:string) : string                = getDocRaw <| api + "/delete" +
                                                         "?email="      + email + 
                                                         "&password="   + password + 
                                                         "&post-id="    + id
                     
// new id out
// TODO: CHANGE THIS TO POST NOT GET
// maybe use stuff like this: http://www.debugging.com/bug/8530
let reblogPost (id:string) (rkey:string) : string  = 
      postDocRaw (api + "/reblog") ("email="      + email + 
                                   "&password="   + password + 
                                   "&post-id="    + id + 
                                   "&reblog-key=" + rkey)


// process XML results /////////////////////////////////////////////

// after getPosts
let processPosts (postsXML) =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
   let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
   let num   = posts.ChildNodes.Count

   // "2010-11-24 05:57:26 GMT"
   let processDate (datestring: string) : System.DateTime =
      (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value |> processDate
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      eprintfn "-> id: %s, rkey: %s, %s\n   %s" id reblogkey (date.ToString()) pic

   // print stats
   eprintfn "Got %d to %d of %d" start (start+num-1) total |> ignore

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


// compose read and process /////////////////////////////////////////////
let readAndProcessPosts = readPosts >> processPosts


// agent /////////////////////////////////////////////

type Message = 
   | ToRead of int * int
   | ToPrint of string
   
let agent = 
   MailboxProcessor.Start(
      fun inbox ->
         let rec loop() = 
            inbox.Scan(
               function 
               | ToRead (start, count) -> Some(async { 
                     eprintfn "reading: (%d, %d)" start count
                     readAndProcessPosts (start, count) |> ignore
                     return! loop() 
                 })
               | ToPrint (s) -> Some(async {
                     do eprintfn "printing: %s" s
                     return! loop()
                 })
            )

         loop()
      )


// range to consider ////////////////////////////////

// get the most recent post on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post /////////////////////////////////////////////
   let dateOfPost (index: int) : System.DateTime = 
      let (start, total, posts) = readAndProcessPosts (index, 1)

      // srsly, TODO: make this post tuple a type
      let (_,_,date,_) = posts |> List.head 
      date

   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (start: int) (target: System.DateTime) : int =
      let nextPostDate = dateOfPost (start-1)
      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch (start-1) target
      
   // binsearch to find latest post before a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      let middle = (newest + oldest) / 2

      let middleDate : System.DateTime = dateOfPost middle

      match (middleDate, target) with
      | a,b when newest =  oldest && a < b -> middle
      | a,b when newest =  oldest && a = b -> walkToNewestMatch middle target
      | a,b when newest =  oldest && a > b -> middle+1
      | a,b when newest <> oldest && a < b -> findCutoff target newest (middle-1)
      | a,b when newest <> oldest && a = b -> walkToNewestMatch middle target    // combine above
      | a,b when newest <> oldest && a > b -> findCutoff target (middle+1) oldest
      | _                                  -> -1 // humbug

   // get the latest post
   let (startingPostNumber, total, posts) = readAndProcessPosts (1, 1)

   // find where the end of the date we care about is
   let oldest = total - 1  // assuming Tumblr numbers from 0
   let newest = findCutoff targetDate startingPostNumber total

   (oldest, newest)


// tests /////////////////////////////////////////////

let testPostReblogging ii = 
   // read some posts
   let (start, total, posts) = readAndProcessPosts (ii, 1)

   // reblog those and delete original posts
   (posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         //deletePost id        |> ignore

         () ))

testPostReblogging 6666;;


(*
testReadAgent() = 
   printfn "a"
   agent.Post <| ToRead (1, 5)  // this 
   printfn "b"
   agent.Post <| ToPrint "omfg" // this and
   printfn "c"
   agent.Post <| ToRead (47, 2) // this do not block
   printfn "d"
*)


let testFindingCutoff() = 
   printfn ""

   let (_,p1) = rangeEndingIn (System.DateTime(2010,12,31))
   printfn "The latest post on or before that date is #%d" p1
   printfn ""

   let (_,p2) = rangeEndingIn (System.DateTime(2011,7,8))
   printfn "The latest post on or before that date is #%d" p2
   printfn ""

   readAndProcessPosts (1187,5) |> ignore

//readAndProcessPosts (7, 2) |> ignore



let deleteOnOrBefore (date: System.DateTime) =
      let (oldest, newest) = rangeEndingIn date
   
      let inc = 30

      [newest..inc..oldest] 
      |> List.map (fun jj -> readAndProcessPosts (jj, inc))
      |> List.map (fun (start,total,posts) -> posts) 
      |> List.concat
      |> List.map (fun (id, rkey, datestring, post) -> 
                     Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore
                     printfn "-* id='%s' rkey='%s'" id rkey
                     printfn "   reblog..."
                     reblogPost id rkey  |> ignore
                     Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore
                     printfn "   delete..."
                     deletePost id       |> ignore
                  )

                  (*
                  *)

            

let testDeletion() =
   deleteOnOrBefore (System.DateTime(2009,6,19))



